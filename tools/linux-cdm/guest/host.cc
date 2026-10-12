#include "content_decryption_module.h"
#include <unistd.h>
#include <sys/random.h>
#include <dlfcn.h>
#include <sys/select.h>
#include <chrono>
#include <iostream>
#include <vector>
#include <algorithm>
#include <limits>
#include <stdexcept>
#include <memory>
#include <charconv>
#include <cstring>
#include <random>
#include <string_view>
#include <unordered_map>
extern "C" {
#include <libavformat/avformat.h>
#include <libavutil/encryption_info.h>
#include <libavutil/base64.h>
}

using Clock = std::chrono::steady_clock;
constexpr size_t MaxPayload = 36 * 1024 * 1024;
std::string quote(const std::string& value) {
    std::string result = "\"";
    for (unsigned char c : value) {
        if (c == '\\' || c == '"') { result += '\\'; result += c; }
        else if (c < 32) { char escaped[7]; snprintf(escaped, sizeof(escaped), "\\u%04x", c); result += escaped; }
        else result += c;
    }
    return result + '"';
}
void encodeInto(std::string& output, const void* input, size_t length) {
    if (length > MaxPayload) throw std::runtime_error("Response exceeds playback limits");
    size_t at = output.size(); output.resize(at + AV_BASE64_SIZE(length));
    av_base64_encode(&output[at], AV_BASE64_SIZE(length), static_cast<const uint8_t*>(input), length);
    output.resize(at + (length + 2) / 3 * 4);
}
std::string encode(const void* input, size_t length) { std::string result; encodeInto(result, input, length); return result; }
struct Token { char* data = nullptr; size_t size = 0; };
// Tokens are NUL-terminated in place by the command parser.
std::vector<uint8_t> decode(const Token& input) {
    if (input.size > MaxPayload * 4 / 3 || input.size % 4) throw std::runtime_error("Invalid base64 payload");
    std::vector<uint8_t> result(input.size / 4 * 3 + 1);
    int length = av_base64_decode(result.data(), input.data, result.size());
    if (length < 0) throw std::runtime_error("Invalid base64 payload");
    result.resize(length); return result;
}
struct MemoryInput { const uint8_t* data; size_t size; size_t position; };
int readMemory(void* opaque, uint8_t* buffer, int size) {
    auto* input = static_cast<MemoryInput*>(opaque);
    size_t count = std::min<size_t>(size, input->size - input->position);
    if (!count) return AVERROR_EOF;
    memcpy(buffer, input->data + input->position, count); input->position += count; return static_cast<int>(count);
}
int64_t seekMemory(void* opaque, int64_t offset, int whence) {
    auto* input = static_cast<MemoryInput*>(opaque); int64_t target;
    switch (whence & ~AVSEEK_FORCE) {
        case AVSEEK_SIZE: return static_cast<int64_t>(input->size);
        case SEEK_SET: target = offset; break;
        case SEEK_CUR: target = static_cast<int64_t>(input->position) + offset; break;
        case SEEK_END: target = static_cast<int64_t>(input->size) + offset; break;
        default: return AVERROR(EINVAL);
    }
    if (target < 0 || target > static_cast<int64_t>(input->size)) return AVERROR(EINVAL);
    input->position = static_cast<size_t>(target); return target;
}
void emit(const std::string& json) { std::cout << json << std::endl; }
void randomFill(uint8_t* data, size_t size) {
    size_t filled = 0;
    while (filled < size) {
        auto count = getrandom(data + filled, size - filled, 0);
        if (count <= 0) break;
        filled += count;
    }
    std::mt19937 generator(static_cast<uint32_t>(Clock::now().time_since_epoch().count()));
    for (; filled < size; ++filled) data[filled] = static_cast<uint8_t>(generator());
}
class Host;
std::unordered_map<std::string, Host*> sessionOwners;
// Promise id of the internal warm-up licence; its callbacks never reach the app
constexpr uint32_t WarmupPromise = std::numeric_limits<uint32_t>::max();
struct Warmup { std::string session; bool created = false, settled = false; };

class Buffer : public cdm::Buffer {
    std::vector<uint8_t> storage; uint32_t size = 0;
public:
    explicit Buffer(uint32_t capacity) : storage(capacity) {}
    void Destroy() override { delete this; }
    uint32_t Capacity() const override { return storage.size(); }
    uint8_t* Data() override { return storage.data(); }
    void SetSize(uint32_t value) override { if (value > storage.size()) throw std::runtime_error("Invalid CDM buffer size"); size = value; }
    uint32_t Size() const override { return size; }
};
class Block : public cdm::DecryptedBlock {
    cdm::Buffer* buffer = nullptr; int64_t timestamp = 0;
public:
    ~Block() { if (buffer) buffer->Destroy(); }
    void SetDecryptedBuffer(cdm::Buffer* value) override { buffer = value; }
    cdm::Buffer* DecryptedBuffer() override { return buffer; }
    void SetTimestamp(int64_t value) override { timestamp = value; }
    int64_t Timestamp() const override { return timestamp; }
};

class Host : public cdm::Host_10 {
    struct Timer { Clock::time_point due; void* context; };
    std::vector<Timer> timers;
public:
    cdm::ContentDecryptionModule_10* cdm = nullptr;
    std::string keyStatuses, certificate;
    bool primary = false, claimed = false, failed = false;
    int initialized = 0;
    Warmup* warmup = nullptr;
    bool warming(const char* session, uint32_t length) const {
        return warmup && (!warmup->created || warmup->session == std::string_view(session, length));
    }
    cdm::Buffer* Allocate(uint32_t bytes) override { return bytes <= MaxPayload ? new Buffer(bytes) : nullptr; }
    void SetTimer(int64_t delay, void* context) override { timers.push_back({Clock::now() + std::chrono::milliseconds(std::max<int64_t>(0, delay)), context}); }
    cdm::Time GetCurrentWallTime() override { return std::chrono::duration<double>(std::chrono::system_clock::now().time_since_epoch()).count(); }
    void OnInitialized(bool success) override {
        initialized = success ? 1 : -1;
        if (primary) emit("{\"event\":\"initialized\",\"protocol\":4,\"success\":" + std::string(success ? "true}" : "false}"));
    }
    void OnResolveKeyStatusPromise(uint32_t id, cdm::KeyStatus status) override { emit("{\"event\":\"resolved\",\"id\":" + std::to_string(id) + ",\"status\":" + std::to_string(status) + "}"); }
    void OnResolveNewSessionPromise(uint32_t id, const char* session, uint32_t length) override {
        if (id == WarmupPromise) {
            if (warmup) { warmup->session.assign(session, length); warmup->created = true; }
            return;
        }
        sessionOwners[std::string(session, length)] = this;
        emit("{\"event\":\"created\",\"id\":" + std::to_string(id) + ",\"session\":" + quote(encode(session, length)) + "}");
    }
    void OnResolvePromise(uint32_t id) override {
        if (id == WarmupPromise) {
            if (warmup) warmup->settled = true;
            return;
        }
        emit("{\"event\":\"resolved\",\"id\":" + std::to_string(id) + "}");
    }
    void OnRejectPromise(uint32_t id, cdm::Exception exception, uint32_t code, const char* message, uint32_t length) override {
        if (id == WarmupPromise) {
            if (warmup) warmup->settled = true;
            return;
        }
        emit("{\"event\":\"rejected\",\"id\":" + std::to_string(id) + ",\"exception\":" + std::to_string(exception) +
            ",\"code\":" + std::to_string(code) + ",\"message\":" + quote(std::string(message, length)) + "}");
    }
    void OnSessionMessage(const char* session, uint32_t sessionLength, cdm::MessageType type, const char* message, uint32_t length) override {
        if (warming(session, sessionLength)) return;
        emit("{\"event\":\"message\",\"session\":" + quote(encode(session, sessionLength)) + ",\"type\":" + std::to_string(type) + ",\"data\":" + quote(encode(message, length)) + "}");
    }
    void OnSessionKeysChange(const char* session, uint32_t length, bool, const cdm::KeyInformation* keys, uint32_t count) override {
        if (warming(session, length)) return;
        std::string statuses; for (uint32_t i = 0; i < count; ++i) statuses += (i ? "," : "") + std::to_string(keys[i].status);
        keyStatuses.clear();
        for (uint32_t i = 0; i < count; ++i) keyStatuses += (i ? "," : "") + encode(keys[i].key_id, keys[i].key_id_size) + ":" + std::to_string(keys[i].status);
        emit("{\"event\":\"keys\",\"session\":" + quote(encode(session, length)) + ",\"statuses\":[" + statuses + "]}");
    }
    void OnExpirationChange(const char* session, uint32_t length, cdm::Time expiry) override {
        if (warming(session, length)) return;
        emit("{\"event\":\"expiration\",\"session\":" + quote(encode(session, length)) + ",\"expiry\":" + std::to_string(expiry) + "}");
    }
    void OnSessionClosed(const char* session, uint32_t length) override {
        if (warming(session, length)) return;
        sessionOwners.erase(std::string(session, length));
        emit("{\"event\":\"closed\",\"session\":" + quote(encode(session, length)) + "}");
    }
    void SendPlatformChallenge(const char*, uint32_t, const char*, uint32_t) override { cdm::PlatformChallengeResponse response{}; cdm->OnPlatformChallengeResponse(response); }
    void EnableOutputProtection(uint32_t) override {}
    void QueryOutputProtectionStatus() override { cdm->OnQueryOutputProtectionStatus(cdm::kQueryFailed, 0, 0); }
    void OnDeferredInitializationDone(cdm::StreamType, cdm::Status) override {}
    cdm::FileIO* CreateFileIO(cdm::FileIOClient*) override { return nullptr; }
    void RequestStorageId(uint32_t version) override { cdm->OnStorageId(version, nullptr, 0); }
    void tick() {
        auto now = Clock::now(); std::vector<void*> ready;
        for (auto it = timers.begin(); it != timers.end();) {
            if (it->due <= now) { ready.push_back(it->context); it = timers.erase(it); } else ++it;
        }
        for (void* context : ready) cdm->TimerExpired(context);
    }
    void fragment(uint32_t id, const Token& payload) {
        auto wire = decode(payload);
        if (wire.size() < 4) throw std::runtime_error("Truncated fragment request");
        size_t init = 0; for (int i = 0; i < 4; ++i) init = (init << 8) | wire[i];
        if (init > wire.size() - 4) throw std::runtime_error("Invalid fragment initialization size");
        uint8_t* data = wire.data() + 4; size_t dataSize = wire.size() - 4;
        MemoryInput memory{data, dataSize, 0};
        constexpr int IoBufferSize = 256 * 1024;
        auto* ioBuffer = static_cast<uint8_t*>(av_malloc(IoBufferSize)); if (!ioBuffer) throw std::bad_alloc();
        AVIOContext* io = avio_alloc_context(ioBuffer, IoBufferSize, 0, &memory, readMemory, nullptr, seekMemory);
        if (!io) { av_free(ioBuffer); throw std::bad_alloc(); }
        auto releaseIo = [&io] { av_freep(&io->buffer); avio_context_free(&io); };
        AVFormatContext* format = avformat_alloc_context();
        if (!format) { releaseIo(); throw std::bad_alloc(); }
        format->pb = io; format->flags |= AVFMT_FLAG_CUSTOM_IO;
        if (avformat_open_input(&format, nullptr, nullptr, nullptr) < 0)
        { releaseIo(); throw std::runtime_error("Unsupported encrypted media fragment"); }
        AVPacket* packet = av_packet_alloc();
        if (!packet) { avformat_close_input(&format); releaseIo(); throw std::bad_alloc(); }
        int decrypted = 0, failed = 0;
        std::string firstFailure;
        try {
            int result;
            while ((result = av_read_frame(format, packet)) >= 0) {
                size_t sideSize = 0; auto* side = av_packet_get_side_data(packet, AV_PKT_DATA_ENCRYPTION_INFO, &sideSize);
                auto* encryption = side ? av_encryption_info_get_side_data(side, sideSize) : nullptr;
                std::unique_ptr<AVEncryptionInfo, decltype(&av_encryption_info_free)> encryptionOwner(encryption, &av_encryption_info_free);
                if (encryption) {
                    cdm::InputBuffer_2 input{};
                    input.data = packet->data; input.data_size = packet->size; input.key_id = encryption->key_id; input.key_id_size = encryption->key_id_size;
                    input.iv = encryption->iv; input.iv_size = encryption->iv_size;
                    if (packet->pts != AV_NOPTS_VALUE)
                        input.timestamp = av_rescale_q(packet->pts, format->streams[packet->stream_index]->time_base, AVRational{1, 1000000});
                    if (encryption->scheme != 0x63626373 && encryption->scheme != 0x63656e63)
                        throw std::runtime_error("Unsupported MP4 encryption scheme");
                    input.encryption_scheme = encryption->scheme == 0x63626373 ? cdm::EncryptionScheme::kCbcs : cdm::EncryptionScheme::kCenc;
                    input.pattern = {encryption->crypt_byte_block, encryption->skip_byte_block};
                    std::vector<cdm::SubsampleEntry> subsamples;
                    uint64_t protectedBytes = encryption->subsample_count ? 0 : packet->size;
                    uint64_t sampleBytes = 0;
                    for (uint32_t i = 0; i < encryption->subsample_count; ++i) {
                        auto sample = encryption->subsamples[i];
                        subsamples.push_back({sample.bytes_of_clear_data, sample.bytes_of_protected_data});
                        protectedBytes += sample.bytes_of_protected_data;
                        sampleBytes += static_cast<uint64_t>(sample.bytes_of_clear_data) + sample.bytes_of_protected_data;
                    }
                    if (encryption->subsample_count && sampleBytes != static_cast<uint32_t>(packet->size)) {
                        throw std::runtime_error("Invalid encrypted sample boundaries");
                    }

                    if (!protectedBytes) { av_packet_unref(packet); continue; }
                    input.subsamples = subsamples.data(); input.num_subsamples = subsamples.size();
                    std::vector<uint8_t> avc;
                    size_t configuration = 0;
                    auto* parameters = format->streams[packet->stream_index]->codecpar;
                    if (parameters->codec_id == AV_CODEC_ID_H264 && parameters->extradata_size >= 5 &&
                        parameters->extradata[0] == 1 && (parameters->extradata[4] & 3) == 3 &&
                        !subsamples.empty() && subsamples.front().clear_bytes >= 4) {

                        avc = {0, 0, 0, 2, 9, 240};
                        configuration = avc.size();
                        avc.insert(avc.end(), packet->data, packet->data + packet->size);
                        subsamples.front().clear_bytes += configuration;
                        input.data = avc.data(); input.data_size = avc.size();
                        input.subsamples = subsamples.data(); input.num_subsamples = subsamples.size();
                    }
                    // Widevine reports kNoKey for subsamples without protected bytes: fold them into the next entry
                    // and leave a trailing clear run out of the input, since those bytes are already plaintext
                    std::vector<cdm::SubsampleEntry> merged; uint32_t trailing = 0;
                    for (auto entry : subsamples) {
                        if (!entry.cipher_bytes) { trailing += entry.clear_bytes; continue; }
                        merged.push_back({trailing + entry.clear_bytes, entry.cipher_bytes}); trailing = 0;
                    }
                    if (!merged.empty()) { input.data_size -= trailing; input.subsamples = merged.data(); input.num_subsamples = merged.size(); }
                    tick();
                    Block block; auto status = cdm->Decrypt(input, &block); auto* plain = block.DecryptedBuffer();
                    if (status == cdm::kSuccess && plain && plain->Size() == input.data_size && packet->pos >= static_cast<int64_t>(init) &&
                        static_cast<uint64_t>(packet->pos) + packet->size <= dataSize) {
                        std::copy(plain->Data() + configuration, plain->Data() + plain->Size(), data + packet->pos); ++decrypted;
                    } else {
                        ++failed;
                        if (firstFailure.empty()) firstFailure = " (status=" + std::to_string(status) +
                            ", position=" + std::to_string(packet->pos) + ", init=" + std::to_string(init) +
                            ", packet=" + std::to_string(packet->size) + ", output=" + std::to_string(plain ? plain->Size() : 0) +
                            ", fragment=" + std::to_string(dataSize - init) + ", protected=" + std::to_string(protectedBytes) +
                            ", keyId=" + encode(encryption->key_id, encryption->key_id_size) + ", statuses=" + keyStatuses + ")";
                    }
                }

                av_packet_unref(packet);
            }
            if (result != AVERROR_EOF) throw std::runtime_error("Encrypted fragment demuxing failed");
            if (failed) throw std::runtime_error("Widevine could not decrypt " + std::to_string(failed) + " media samples" + firstFailure);
            av_packet_free(&packet); avformat_close_input(&format); releaseIo();
            std::string output = "{\"event\":\"fragment\",\"id\":" + std::to_string(id) + ",\"samples\":" + std::to_string(decrypted) + ",\"data\":\"";
            output.reserve(output.size() + AV_BASE64_SIZE(dataSize - init) + 3);
            encodeInto(output, data + init, dataSize - init); output += "\"}\n";
            std::cout.write(output.data(), output.size()); std::cout.flush();
        } catch (...) { av_packet_free(&packet); avformat_close_input(&format); releaseIo(); throw; }
    }
};

void* getHost(int version, void* data) { return version == 10 ? static_cast<cdm::Host_10*>(static_cast<Host*>(data)) : nullptr; }
int main(int argc, char** argv) {
    if (argc != 2) return 2;
    void* library = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (!library) { std::cerr << dlerror() << std::endl; return 3; }
    auto initialize = reinterpret_cast<void (*)()>(dlsym(library, "InitializeCdmModule_4"));
    auto create = reinterpret_cast<decltype(&CreateCdmInstance)>(dlsym(library, "CreateCdmInstance"));
    auto deinitialize = reinterpret_cast<void (*)()>(dlsym(library, "DeinitializeCdmModule"));
    if (!initialize || !create) return 4;
    initialize();
    // One CDM instance per service certificate; instances live until exit, since destroying one with sessions crashes
    constexpr size_t MaxInstances = 4;
    std::vector<std::unique_ptr<Host>> hosts;
    auto spawn = [&]() -> Host* {
        hosts.push_back(std::make_unique<Host>());
        Host* host = hosts.back().get();
        host->primary = hosts.size() == 1;
        host->cdm = static_cast<cdm::ContentDecryptionModule_10*>(create(10, "com.widevine.alpha", 18, getHost, host));
        if (!host->cdm) { hosts.pop_back(); return nullptr; }
        host->cdm->Initialize(false, false, false);
        if (host->initialized < 0) { host->failed = true; return nullptr; }
        return host;
    };
    spawn();
    if (hosts.empty()) return 5;
    Host* active = hosts[0].get();
    auto tickAll = [&] { for (auto& host : hosts) host->tick(); };
    auto owner = [&](const std::vector<uint8_t>& session) {
        auto found = sessionOwners.find(std::string(session.begin(), session.end()));
        return found == sessionOwners.end() ? active : found->second;
    };
    // Rejected synthetic licence on a throwaway session: pays the first-licence cost before playback
    auto warm = [&](Host& host, const std::vector<uint8_t>& pssh) {
        Warmup state; host.warmup = &state;
        auto wait = [&](auto done) {
            auto deadline = Clock::now() + std::chrono::seconds(30);
            while (!done() && Clock::now() < deadline) { tickAll(); usleep(1000); }
        };
        host.cdm->CreateSessionAndGenerateRequest(WarmupPromise, cdm::kTemporary, cdm::kCenc, pssh.data(), pssh.size());
        wait([&] { return state.created || state.settled; });
        bool created = state.created;
        if (created) {
            std::vector<uint8_t> licence(5 + 338 + 2 + 32);
            const uint8_t header[] = {0x08, 0x02, 0x12, 0xd2, 0x02};
            std::copy(std::begin(header), std::end(header), licence.begin());
            randomFill(licence.data() + 5, 338);
            licence[343] = 0x1a; licence[344] = 0x20;
            randomFill(licence.data() + 345, 32);
            state.settled = false;
            host.cdm->UpdateSession(WarmupPromise, state.session.data(), state.session.size(), licence.data(), licence.size());
            wait([&] { return state.settled; });
            state.settled = false;
            host.cdm->CloseSession(WarmupPromise, state.session.data(), state.session.size());
            wait([&] { return state.settled; });
        }
        host.warmup = nullptr;
        return created;
    };
    std::string pending; size_t scanned = 0; bool running = true; auto lastInput = Clock::now();
    std::vector<char> buffer(1024 * 1024);
    while (running && Clock::now() - lastInput < std::chrono::seconds(30)) {
        tickAll(); fd_set ready; FD_ZERO(&ready); FD_SET(0, &ready); timeval timeout{0, 10000};
        if (select(1, &ready, nullptr, nullptr, &timeout) <= 0) continue;
        auto length = read(0, buffer.data(), buffer.size()); if (length <= 0) break;
        lastInput = Clock::now(); pending.append(buffer.data(), length);
        if (pending.size() > MaxPayload * 4 / 3 + 1024) break;
        size_t newline, consumed = 0;
        while ((newline = pending.find('\n', std::max(scanned, consumed))) != std::string::npos) {
            char* cursor = pending.data() + consumed; char* end = pending.data() + newline; *end = '\0';
            consumed = newline + 1;
            Token tokens[4]; size_t count = 0;
            auto separator = [](char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\v' || c == '\f'; };
            while (cursor < end && count < 4) {
                while (cursor < end && separator(*cursor)) *cursor++ = '\0';
                if (cursor == end) break;
                char* start = cursor; while (cursor < end && !separator(*cursor)) ++cursor;
                tokens[count++] = {start, static_cast<size_t>(cursor - start)};
                if (cursor < end) *cursor++ = '\0';
            }
            std::string_view command = count ? std::string_view(tokens[0].data, tokens[0].size) : std::string_view();
            const Token& first = tokens[2]; const Token& second = tokens[3]; uint32_t id = 0;
            if (command == "QUIT") { running = false; break; }
            if (command == "PING") continue;
            try {
                bool parsed = false;
                if (count >= 2) { auto result = std::from_chars(tokens[1].data, tokens[1].data + tokens[1].size, id); parsed = result.ec == std::errc() && result.ptr == tokens[1].data + tokens[1].size; }
                // CERT without a payload selects the instance without a service certificate
                if (!parsed || !id || id == WarmupPromise || (count < 3 && command != "CERT")) throw std::runtime_error("Invalid playback command");
                if (command == "SESSION") { auto data = decode(first); active->cdm->CreateSessionAndGenerateRequest(id, cdm::kTemporary, cdm::kCenc, data.data(), data.size()); }
                else if (command == "CERT") {
                    std::vector<uint8_t> data;
                    if (count >= 3) data = decode(first);
                    std::string certificate(data.begin(), data.end());
                    Host* target = nullptr;
                    for (auto& host : hosts) {
                        if (host->claimed && !host->failed && host->certificate == certificate) target = host.get();
                    }
                    bool fresh = target == nullptr;
                    if (fresh) {
                        if (!hosts[0]->claimed) target = hosts[0].get();
                        else if (hosts.size() >= MaxInstances) throw std::runtime_error("instance limit");
                        else if (!(target = spawn())) throw std::runtime_error("Widevine initialization failed");
                        target->claimed = true; target->certificate = certificate;
                    }
                    active = target;
                    if (fresh && !data.empty()) target->cdm->SetServerCertificate(id, data.data(), data.size());
                    else emit("{\"event\":\"resolved\",\"id\":" + std::to_string(id) + "}");
                }
                else if (command == "WARMUP") {
                    auto pssh = decode(first);
                    bool created = warm(*hosts[0], pssh);
                    emit("{\"event\":\"resolved\",\"id\":" + std::to_string(id) + ",\"warm\":" + (created ? "true}" : "false}"));
                }
                else if (command == "UPDATE") {
                    if (count < 4) throw std::runtime_error("Missing license response");
                    auto session = decode(first), data = decode(second);
                    owner(session)->cdm->UpdateSession(id, reinterpret_cast<const char*>(session.data()), session.size(), data.data(), data.size());
                }
                else if (command == "CLOSE") {
                    auto session = decode(first);
                    owner(session)->cdm->CloseSession(id, reinterpret_cast<const char*>(session.data()), session.size());
                }
                else if (command == "FRAGMENT") active->fragment(id, first);
                else throw std::runtime_error("Unknown playback command");
            } catch (const std::exception& error) { emit("{\"event\":\"error\",\"id\":" + std::to_string(id) + ",\"message\":" + quote(error.what()) + "}"); }
        }
        pending.erase(0, consumed); scanned = pending.size();
        lastInput = Clock::now();
    }
    for (auto host = hosts.rbegin(); host != hosts.rend(); ++host) (*host)->cdm->Destroy();
    if (deinitialize) deinitialize();
    dlclose(library);
    return 0;
}
