

#ifndef CDM_CONTENT_DECRYPTION_MODULE_H_
#define CDM_CONTENT_DECRYPTION_MODULE_H_

#include <type_traits>

#include "content_decryption_module_export.h"
#include "content_decryption_module_proxy.h"

#if defined(_MSC_VER)
typedef unsigned char uint8_t;
typedef unsigned int uint32_t;
typedef int int32_t;
typedef __int64 int64_t;
#else
#include <stdint.h>
#endif

#define CDM_MODULE_VERSION 4

#define INITIALIZE_CDM_MODULE \
  BUILD_ENTRYPOINT(InitializeCdmModule, CDM_MODULE_VERSION)
#define BUILD_ENTRYPOINT(name, version) \
  BUILD_ENTRYPOINT_NO_EXPANSION(name, version)
#define BUILD_ENTRYPOINT_NO_EXPANSION(name, version) name##_##version

#define CHECK_TYPE(type, size_32, size_64)                           \
  static_assert(std::is_standard_layout<type>(),                     \
                #type " not standard_layout");                       \
  static_assert(std::is_trivial<type>(), #type " not trivial");      \
  static_assert((sizeof(void*) == 4 && sizeof(type) == size_32) ||   \
                    (sizeof(void*) == 8 && sizeof(type) == size_64), \
                #type " size mismatch")

extern "C" {

CDM_API void INITIALIZE_CDM_MODULE();

CDM_API void DeinitializeCdmModule();

typedef void* (*GetCdmHostFunc)(int host_interface_version, void* user_data);

CDM_API void* CreateCdmInstance(int cdm_interface_version,
                                const char* key_system,
                                uint32_t key_system_size,
                                GetCdmHostFunc get_cdm_host_func,
                                void* user_data);

CDM_API const char* GetCdmVersion();

}

namespace cdm {

enum Status : uint32_t {
  kSuccess = 0,
  kNeedMoreData,
  kNoKey,
  kInitializationError,
  kDecryptError,
  kDecodeError,
  kDeferredInitialization
};
CHECK_TYPE(Status, 4, 4);

enum Exception : uint32_t {
  kExceptionTypeError,
  kExceptionNotSupportedError,
  kExceptionInvalidStateError,
  kExceptionQuotaExceededError
};
CHECK_TYPE(Exception, 4, 4);

enum class EncryptionScheme : uint32_t {
  kUnencrypted = 0,
  kCenc,
  kCbcs
};
CHECK_TYPE(EncryptionScheme, 4, 4);

struct Pattern {
  uint32_t crypt_byte_block;
  uint32_t skip_byte_block;
};
CHECK_TYPE(Pattern, 8, 8);

enum class ColorRange : uint8_t {
  kInvalid,
  kLimited,
  kFull,
  kDerived
};
CHECK_TYPE(ColorRange, 1, 1);

struct ColorSpace {
  uint8_t primary_id;
  uint8_t transfer_id;
  uint8_t matrix_id;
  ColorRange range;
};
CHECK_TYPE(ColorSpace, 4, 4);

typedef double Time;

struct SubsampleEntry {
  uint32_t clear_bytes;
  uint32_t cipher_bytes;
};
CHECK_TYPE(SubsampleEntry, 8, 8);

struct InputBuffer_1 {
  const uint8_t* data;
  uint32_t data_size;

  const uint8_t* key_id;
  uint32_t key_id_size;

  const uint8_t* iv;
  uint32_t iv_size;

  const struct SubsampleEntry* subsamples;
  uint32_t num_subsamples;

  int64_t timestamp;
};
CHECK_TYPE(InputBuffer_1, 40, 72);

struct InputBuffer_2 {
  const uint8_t* data;
  uint32_t data_size;

  EncryptionScheme encryption_scheme;

  const uint8_t* key_id;
  uint32_t key_id_size;
  uint32_t : 32;

  const uint8_t* iv;
  uint32_t iv_size;
  uint32_t : 32;

  const struct SubsampleEntry* subsamples;
  uint32_t num_subsamples;
  uint32_t : 32;

  Pattern pattern;

  int64_t timestamp;
};
CHECK_TYPE(InputBuffer_2, 64, 80);

enum AudioCodec : uint32_t { kUnknownAudioCodec = 0, kCodecVorbis, kCodecAac };
CHECK_TYPE(AudioCodec, 4, 4);

struct AudioDecoderConfig_1 {
  AudioCodec codec;
  int32_t channel_count;
  int32_t bits_per_channel;
  int32_t samples_per_second;

  uint8_t* extra_data;
  uint32_t extra_data_size;
};
CHECK_TYPE(AudioDecoderConfig_1, 24, 32);

struct AudioDecoderConfig_2 {
  AudioCodec codec;
  int32_t channel_count;
  int32_t bits_per_channel;
  int32_t samples_per_second;

  uint8_t* extra_data;
  uint32_t extra_data_size;

  EncryptionScheme encryption_scheme;
};
CHECK_TYPE(AudioDecoderConfig_2, 28, 32);

enum AudioFormat : uint32_t {
  kUnknownAudioFormat = 0,
  kAudioFormatU8,
  kAudioFormatS16,
  kAudioFormatS32,
  kAudioFormatF32,
  kAudioFormatPlanarS16,
  kAudioFormatPlanarF32,
};
CHECK_TYPE(AudioFormat, 4, 4);

enum VideoFormat : uint32_t {
  kUnknownVideoFormat = 0,
  kYv12 = 1,
  kI420 = 2,

  kYUV420P9 = 16,
  kYUV420P10 = 17,
  kYUV422P9 = 18,
  kYUV422P10 = 19,
  kYUV444P9 = 20,
  kYUV444P10 = 21,
  kYUV420P12 = 22,
  kYUV422P12 = 23,
  kYUV444P12 = 24,
};
CHECK_TYPE(VideoFormat, 4, 4);

struct Size {
  int32_t width;
  int32_t height;
};
CHECK_TYPE(Size, 8, 8);

enum VideoCodec : uint32_t {
  kUnknownVideoCodec = 0,
  kCodecVp8,
  kCodecH264,
  kCodecVp9,
  kCodecAv1
};
CHECK_TYPE(VideoCodec, 4, 4);

enum VideoCodecProfile : uint32_t {
  kUnknownVideoCodecProfile = 0,
  kProfileNotNeeded,
  kH264ProfileBaseline,
  kH264ProfileMain,
  kH264ProfileExtended,
  kH264ProfileHigh,
  kH264ProfileHigh10,
  kH264ProfileHigh422,
  kH264ProfileHigh444Predictive,

  kVP9Profile0,
  kVP9Profile1,
  kVP9Profile2,
  kVP9Profile3,
  kAv1ProfileMain,
  kAv1ProfileHigh,
  kAv1ProfilePro
};
CHECK_TYPE(VideoCodecProfile, 4, 4);

struct VideoDecoderConfig_1 {
  VideoCodec codec;
  VideoCodecProfile profile;
  VideoFormat format;

  Size coded_size;

  uint8_t* extra_data;
  uint32_t extra_data_size;
};
CHECK_TYPE(VideoDecoderConfig_1, 28, 40);

struct VideoDecoderConfig_2 {
  VideoCodec codec;
  VideoCodecProfile profile;
  VideoFormat format;
  uint32_t : 32;

  Size coded_size;

  uint8_t* extra_data;
  uint32_t extra_data_size;

  EncryptionScheme encryption_scheme;
};
CHECK_TYPE(VideoDecoderConfig_2, 36, 40);

struct VideoDecoderConfig_3 {
  VideoCodec codec;
  VideoCodecProfile profile;
  VideoFormat format;
  ColorSpace color_space;

  Size coded_size;

  uint8_t* extra_data;
  uint32_t extra_data_size;

  EncryptionScheme encryption_scheme;
};
CHECK_TYPE(VideoDecoderConfig_3, 36, 40);

enum StreamType : uint32_t { kStreamTypeAudio = 0, kStreamTypeVideo = 1 };
CHECK_TYPE(StreamType, 4, 4);

struct PlatformChallengeResponse {

  const uint8_t* signed_data;
  uint32_t signed_data_length;

  const uint8_t* signed_data_signature;
  uint32_t signed_data_signature_length;

  const uint8_t* platform_key_certificate;
  uint32_t platform_key_certificate_length;
};
CHECK_TYPE(PlatformChallengeResponse, 24, 48);

enum KeyStatus : uint32_t {
  kUsable = 0,
  kInternalError = 1,
  kExpired = 2,
  kOutputRestricted = 3,
  kOutputDownscaled = 4,
  kStatusPending = 5,
  kReleased = 6
};
CHECK_TYPE(KeyStatus, 4, 4);

struct KeyInformation {
  const uint8_t* key_id;
  uint32_t key_id_size;
  KeyStatus status;
  uint32_t system_code;
};
CHECK_TYPE(KeyInformation, 16, 24);

enum OutputProtectionMethods : uint32_t {
  kProtectionNone = 0,
  kProtectionHDCP = 1 << 0
};
CHECK_TYPE(OutputProtectionMethods, 4, 4);

enum OutputLinkTypes : uint32_t {
  kLinkTypeNone = 0,
  kLinkTypeUnknown = 1 << 0,
  kLinkTypeInternal = 1 << 1,
  kLinkTypeVGA = 1 << 2,
  kLinkTypeHDMI = 1 << 3,
  kLinkTypeDVI = 1 << 4,
  kLinkTypeDisplayPort = 1 << 5,
  kLinkTypeNetwork = 1 << 6
};
CHECK_TYPE(OutputLinkTypes, 4, 4);

enum QueryResult : uint32_t { kQuerySucceeded = 0, kQueryFailed };
CHECK_TYPE(QueryResult, 4, 4);

enum InitDataType : uint32_t { kCenc = 0, kKeyIds = 1, kWebM = 2 };
CHECK_TYPE(InitDataType, 4, 4);

enum SessionType : uint32_t {
  kTemporary = 0,
  kPersistentLicense = 1,
  kPersistentUsageRecord = 2
};
CHECK_TYPE(SessionType, 4, 4);

enum MessageType : uint32_t {
  kLicenseRequest = 0,
  kLicenseRenewal = 1,
  kLicenseRelease = 2,

  kIndividualizationRequest = 3
};
CHECK_TYPE(MessageType, 4, 4);

enum HdcpVersion : uint32_t {
  kHdcpVersionNone,
  kHdcpVersion1_0,
  kHdcpVersion1_1,
  kHdcpVersion1_2,
  kHdcpVersion1_3,
  kHdcpVersion1_4,
  kHdcpVersion2_0,
  kHdcpVersion2_1,
  kHdcpVersion2_2,
  kHdcpVersion2_3
};
CHECK_TYPE(HdcpVersion, 4, 4);

struct Policy {
  HdcpVersion min_hdcp_version;
};
CHECK_TYPE(Policy, 4, 4);

class CDM_CLASS_API Buffer {
 public:

  virtual void Destroy() = 0;

  virtual uint32_t Capacity() const = 0;
  virtual uint8_t* Data() = 0;
  virtual void SetSize(uint32_t size) = 0;
  virtual uint32_t Size() const = 0;

 protected:
  Buffer() {}
  virtual ~Buffer() {}

 private:
  Buffer(const Buffer&);
  void operator=(const Buffer&);
};

class CDM_CLASS_API DecryptedBlock {
 public:
  virtual void SetDecryptedBuffer(Buffer* buffer) = 0;
  virtual Buffer* DecryptedBuffer() = 0;

  virtual void SetTimestamp(int64_t timestamp) = 0;
  virtual int64_t Timestamp() const = 0;

 protected:
  DecryptedBlock() {}
  virtual ~DecryptedBlock() {}
};

enum VideoPlane : uint32_t {
  kYPlane = 0,
  kUPlane = 1,
  kVPlane = 2,
  kMaxPlanes = 3,
};
CHECK_TYPE(VideoPlane, 4, 4);

class CDM_CLASS_API VideoFrame {
 public:
  virtual void SetFormat(VideoFormat format) = 0;
  virtual VideoFormat Format() const = 0;

  virtual void SetSize(cdm::Size size) = 0;
  virtual cdm::Size Size() const = 0;

  virtual void SetFrameBuffer(Buffer* frame_buffer) = 0;
  virtual Buffer* FrameBuffer() = 0;

  virtual void SetPlaneOffset(VideoPlane plane, uint32_t offset) = 0;
  virtual uint32_t PlaneOffset(VideoPlane plane) = 0;

  virtual void SetStride(VideoPlane plane, uint32_t stride) = 0;
  virtual uint32_t Stride(VideoPlane plane) = 0;

  virtual void SetTimestamp(int64_t timestamp) = 0;
  virtual int64_t Timestamp() const = 0;

 protected:
  VideoFrame() {}
  virtual ~VideoFrame() {}
};

class CDM_CLASS_API VideoFrame_2 {
 public:
  virtual void SetFormat(VideoFormat format) = 0;
  virtual void SetSize(cdm::Size size) = 0;
  virtual void SetFrameBuffer(Buffer* frame_buffer) = 0;
  virtual void SetPlaneOffset(VideoPlane plane, uint32_t offset) = 0;
  virtual void SetStride(VideoPlane plane, uint32_t stride) = 0;

  virtual void SetTimestamp(int64_t timestamp) = 0;
  virtual void SetColorSpace(ColorSpace color_space) = 0;

 protected:
  VideoFrame_2() {}
  virtual ~VideoFrame_2() {}
};

class CDM_CLASS_API AudioFrames {
 public:
  virtual void SetFrameBuffer(Buffer* buffer) = 0;
  virtual Buffer* FrameBuffer() = 0;

  virtual void SetFormat(AudioFormat format) = 0;
  virtual AudioFormat Format() const = 0;

 protected:
  AudioFrames() {}
  virtual ~AudioFrames() {}
};

class CDM_CLASS_API FileIO {
 public:

  virtual void Open(const char* file_name, uint32_t file_name_size) = 0;

  virtual void Read() = 0;

  virtual void Write(const uint8_t* data, uint32_t data_size) = 0;

  virtual void Close() = 0;

 protected:
  FileIO() {}
  virtual ~FileIO() {}
};

class CDM_CLASS_API FileIOClient {
 public:
  enum class Status : uint32_t { kSuccess = 0, kInUse, kError };

  virtual void OnOpenComplete(Status status) = 0;

  virtual void OnReadComplete(Status status,
                              const uint8_t* data,
                              uint32_t data_size) = 0;

  virtual void OnWriteComplete(Status status) = 0;

 protected:
  FileIOClient() {}
  virtual ~FileIOClient() {}
};

class CDM_CLASS_API Host_9;
class CDM_CLASS_API Host_10;
class CDM_CLASS_API Host_11;

class CDM_CLASS_API ContentDecryptionModule_9 {
 public:
  static const int kVersion = 9;
  typedef Host_9 Host;

  virtual void Initialize(bool allow_distinctive_identifier,
                          bool allow_persistent_state) = 0;

  virtual void GetStatusForPolicy(uint32_t promise_id,
                                  const Policy& policy) = 0;

  virtual void SetServerCertificate(uint32_t promise_id,
                                    const uint8_t* server_certificate_data,
                                    uint32_t server_certificate_data_size) = 0;

  virtual void CreateSessionAndGenerateRequest(uint32_t promise_id,
                                               SessionType session_type,
                                               InitDataType init_data_type,
                                               const uint8_t* init_data,
                                               uint32_t init_data_size) = 0;

  virtual void LoadSession(uint32_t promise_id,
                           SessionType session_type,
                           const char* session_id,
                           uint32_t session_id_size) = 0;

  virtual void UpdateSession(uint32_t promise_id,
                             const char* session_id,
                             uint32_t session_id_size,
                             const uint8_t* response,
                             uint32_t response_size) = 0;

  virtual void CloseSession(uint32_t promise_id,
                            const char* session_id,
                            uint32_t session_id_size) = 0;

  virtual void RemoveSession(uint32_t promise_id,
                             const char* session_id,
                             uint32_t session_id_size) = 0;

  virtual void TimerExpired(void* context) = 0;

  virtual Status Decrypt(const InputBuffer_1& encrypted_buffer,
                         DecryptedBlock* decrypted_buffer) = 0;

  virtual Status InitializeAudioDecoder(
      const AudioDecoderConfig_1& audio_decoder_config) = 0;

  virtual Status InitializeVideoDecoder(
      const VideoDecoderConfig_1& video_decoder_config) = 0;

  virtual void DeinitializeDecoder(StreamType decoder_type) = 0;

  virtual void ResetDecoder(StreamType decoder_type) = 0;

  virtual Status DecryptAndDecodeFrame(const InputBuffer_1& encrypted_buffer,
                                       VideoFrame* video_frame) = 0;

  virtual Status DecryptAndDecodeSamples(const InputBuffer_1& encrypted_buffer,
                                         AudioFrames* audio_frames) = 0;

  virtual void OnPlatformChallengeResponse(
      const PlatformChallengeResponse& response) = 0;

  virtual void OnQueryOutputProtectionStatus(
      QueryResult result,
      uint32_t link_mask,
      uint32_t output_protection_mask) = 0;

  virtual void OnStorageId(uint32_t version,
                           const uint8_t* storage_id,
                           uint32_t storage_id_size) = 0;

  virtual void Destroy() = 0;

 protected:
  ContentDecryptionModule_9() {}
  virtual ~ContentDecryptionModule_9() {}
};

class CDM_CLASS_API ContentDecryptionModule_10 {
 public:
  static const int kVersion = 10;
  static const bool kIsStable = true;
  typedef Host_10 Host;

  virtual void Initialize(bool allow_distinctive_identifier,
                          bool allow_persistent_state,
                          bool use_hw_secure_codecs) = 0;

  virtual void GetStatusForPolicy(uint32_t promise_id,
                                  const Policy& policy) = 0;

  virtual void SetServerCertificate(uint32_t promise_id,
                                    const uint8_t* server_certificate_data,
                                    uint32_t server_certificate_data_size) = 0;

  virtual void CreateSessionAndGenerateRequest(uint32_t promise_id,
                                               SessionType session_type,
                                               InitDataType init_data_type,
                                               const uint8_t* init_data,
                                               uint32_t init_data_size) = 0;

  virtual void LoadSession(uint32_t promise_id,
                           SessionType session_type,
                           const char* session_id,
                           uint32_t session_id_size) = 0;

  virtual void UpdateSession(uint32_t promise_id,
                             const char* session_id,
                             uint32_t session_id_size,
                             const uint8_t* response,
                             uint32_t response_size) = 0;

  virtual void CloseSession(uint32_t promise_id,
                            const char* session_id,
                            uint32_t session_id_size) = 0;

  virtual void RemoveSession(uint32_t promise_id,
                             const char* session_id,
                             uint32_t session_id_size) = 0;

  virtual void TimerExpired(void* context) = 0;

  virtual Status Decrypt(const InputBuffer_2& encrypted_buffer,
                         DecryptedBlock* decrypted_buffer) = 0;

  virtual Status InitializeAudioDecoder(
      const AudioDecoderConfig_2& audio_decoder_config) = 0;

  virtual Status InitializeVideoDecoder(
      const VideoDecoderConfig_2& video_decoder_config) = 0;

  virtual void DeinitializeDecoder(StreamType decoder_type) = 0;

  virtual void ResetDecoder(StreamType decoder_type) = 0;

  virtual Status DecryptAndDecodeFrame(const InputBuffer_2& encrypted_buffer,
                                       VideoFrame* video_frame) = 0;

  virtual Status DecryptAndDecodeSamples(const InputBuffer_2& encrypted_buffer,
                                         AudioFrames* audio_frames) = 0;

  virtual void OnPlatformChallengeResponse(
      const PlatformChallengeResponse& response) = 0;

  virtual void OnQueryOutputProtectionStatus(
      QueryResult result,
      uint32_t link_mask,
      uint32_t output_protection_mask) = 0;

  virtual void OnStorageId(uint32_t version,
                           const uint8_t* storage_id,
                           uint32_t storage_id_size) = 0;

  virtual void Destroy() = 0;

 protected:
  ContentDecryptionModule_10() {}
  virtual ~ContentDecryptionModule_10() {}
};

class CDM_CLASS_API ContentDecryptionModule_11 {
 public:
  static const int kVersion = 11;
  static const bool kIsStable = false;
  typedef Host_11 Host;

  virtual void Initialize(bool allow_distinctive_identifier,
                          bool allow_persistent_state,
                          bool use_hw_secure_codecs) = 0;

  virtual void GetStatusForPolicy(uint32_t promise_id,
                                  const Policy& policy) = 0;

  virtual void SetServerCertificate(uint32_t promise_id,
                                    const uint8_t* server_certificate_data,
                                    uint32_t server_certificate_data_size) = 0;

  virtual void CreateSessionAndGenerateRequest(uint32_t promise_id,
                                               SessionType session_type,
                                               InitDataType init_data_type,
                                               const uint8_t* init_data,
                                               uint32_t init_data_size) = 0;

  virtual void LoadSession(uint32_t promise_id,
                           SessionType session_type,
                           const char* session_id,
                           uint32_t session_id_size) = 0;

  virtual void UpdateSession(uint32_t promise_id,
                             const char* session_id,
                             uint32_t session_id_size,
                             const uint8_t* response,
                             uint32_t response_size) = 0;

  virtual void CloseSession(uint32_t promise_id,
                            const char* session_id,
                            uint32_t session_id_size) = 0;

  virtual void RemoveSession(uint32_t promise_id,
                             const char* session_id,
                             uint32_t session_id_size) = 0;

  virtual void TimerExpired(void* context) = 0;

  virtual Status Decrypt(const InputBuffer_2& encrypted_buffer,
                         DecryptedBlock* decrypted_buffer) = 0;

  virtual Status InitializeAudioDecoder(
      const AudioDecoderConfig_2& audio_decoder_config) = 0;

  virtual Status InitializeVideoDecoder(
      const VideoDecoderConfig_3& video_decoder_config) = 0;

  virtual void DeinitializeDecoder(StreamType decoder_type) = 0;

  virtual void ResetDecoder(StreamType decoder_type) = 0;

  virtual Status DecryptAndDecodeFrame(const InputBuffer_2& encrypted_buffer,
                                       VideoFrame_2* video_frame) = 0;

  virtual Status DecryptAndDecodeSamples(const InputBuffer_2& encrypted_buffer,
                                         AudioFrames* audio_frames) = 0;

  virtual void OnPlatformChallengeResponse(
      const PlatformChallengeResponse& response) = 0;

  virtual void OnQueryOutputProtectionStatus(
      QueryResult result,
      uint32_t link_mask,
      uint32_t output_protection_mask) = 0;

  virtual void OnStorageId(uint32_t version,
                           const uint8_t* storage_id,
                           uint32_t storage_id_size) = 0;

  virtual void Destroy() = 0;

 protected:
  ContentDecryptionModule_11() {}
  virtual ~ContentDecryptionModule_11() {}
};

class CDM_CLASS_API Host_9 {
 public:
  static const int kVersion = 9;

  virtual Buffer* Allocate(uint32_t capacity) = 0;

  virtual void SetTimer(int64_t delay_ms, void* context) = 0;

  virtual Time GetCurrentWallTime() = 0;

  virtual void OnResolveKeyStatusPromise(uint32_t promise_id,
                                         KeyStatus key_status) = 0;

  virtual void OnResolveNewSessionPromise(uint32_t promise_id,
                                          const char* session_id,
                                          uint32_t session_id_size) = 0;

  virtual void OnResolvePromise(uint32_t promise_id) = 0;

  virtual void OnRejectPromise(uint32_t promise_id,
                               Exception exception,
                               uint32_t system_code,
                               const char* error_message,
                               uint32_t error_message_size) = 0;

  virtual void OnSessionMessage(const char* session_id,
                                uint32_t session_id_size,
                                MessageType message_type,
                                const char* message,
                                uint32_t message_size) = 0;

  virtual void OnSessionKeysChange(const char* session_id,
                                   uint32_t session_id_size,
                                   bool has_additional_usable_key,
                                   const KeyInformation* keys_info,
                                   uint32_t keys_info_count) = 0;

  virtual void OnExpirationChange(const char* session_id,
                                  uint32_t session_id_size,
                                  Time new_expiry_time) = 0;

  virtual void OnSessionClosed(const char* session_id,
                               uint32_t session_id_size) = 0;

  virtual void SendPlatformChallenge(const char* service_id,
                                     uint32_t service_id_size,
                                     const char* challenge,
                                     uint32_t challenge_size) = 0;

  virtual void EnableOutputProtection(uint32_t desired_protection_mask) = 0;

  virtual void QueryOutputProtectionStatus() = 0;

  virtual void OnDeferredInitializationDone(StreamType stream_type,
                                            Status decoder_status) = 0;

  virtual FileIO* CreateFileIO(FileIOClient* client) = 0;

  virtual void RequestStorageId(uint32_t version) = 0;

 protected:
  Host_9() {}
  virtual ~Host_9() {}
};

class CDM_CLASS_API Host_10 {
 public:
  static const int kVersion = 10;

  virtual Buffer* Allocate(uint32_t capacity) = 0;

  virtual void SetTimer(int64_t delay_ms, void* context) = 0;

  virtual Time GetCurrentWallTime() = 0;

  virtual void OnInitialized(bool success) = 0;

  virtual void OnResolveKeyStatusPromise(uint32_t promise_id,
                                         KeyStatus key_status) = 0;

  virtual void OnResolveNewSessionPromise(uint32_t promise_id,
                                          const char* session_id,
                                          uint32_t session_id_size) = 0;

  virtual void OnResolvePromise(uint32_t promise_id) = 0;

  virtual void OnRejectPromise(uint32_t promise_id,
                               Exception exception,
                               uint32_t system_code,
                               const char* error_message,
                               uint32_t error_message_size) = 0;

  virtual void OnSessionMessage(const char* session_id,
                                uint32_t session_id_size,
                                MessageType message_type,
                                const char* message,
                                uint32_t message_size) = 0;

  virtual void OnSessionKeysChange(const char* session_id,
                                   uint32_t session_id_size,
                                   bool has_additional_usable_key,
                                   const KeyInformation* keys_info,
                                   uint32_t keys_info_count) = 0;

  virtual void OnExpirationChange(const char* session_id,
                                  uint32_t session_id_size,
                                  Time new_expiry_time) = 0;

  virtual void OnSessionClosed(const char* session_id,
                               uint32_t session_id_size) = 0;

  virtual void SendPlatformChallenge(const char* service_id,
                                     uint32_t service_id_size,
                                     const char* challenge,
                                     uint32_t challenge_size) = 0;

  virtual void EnableOutputProtection(uint32_t desired_protection_mask) = 0;

  virtual void QueryOutputProtectionStatus() = 0;

  virtual void OnDeferredInitializationDone(StreamType stream_type,
                                            Status decoder_status) = 0;

  virtual FileIO* CreateFileIO(FileIOClient* client) = 0;

  virtual void RequestStorageId(uint32_t version) = 0;

 protected:
  Host_10() {}
  virtual ~Host_10() {}
};

class CDM_CLASS_API Host_11 {
 public:
  static const int kVersion = 11;

  virtual Buffer* Allocate(uint32_t capacity) = 0;

  virtual void SetTimer(int64_t delay_ms, void* context) = 0;

  virtual Time GetCurrentWallTime() = 0;

  virtual void OnInitialized(bool success) = 0;

  virtual void OnResolveKeyStatusPromise(uint32_t promise_id,
                                         KeyStatus key_status) = 0;

  virtual void OnResolveNewSessionPromise(uint32_t promise_id,
                                          const char* session_id,
                                          uint32_t session_id_size) = 0;

  virtual void OnResolvePromise(uint32_t promise_id) = 0;

  virtual void OnRejectPromise(uint32_t promise_id,
                               Exception exception,
                               uint32_t system_code,
                               const char* error_message,
                               uint32_t error_message_size) = 0;

  virtual void OnSessionMessage(const char* session_id,
                                uint32_t session_id_size,
                                MessageType message_type,
                                const char* message,
                                uint32_t message_size) = 0;

  virtual void OnSessionKeysChange(const char* session_id,
                                   uint32_t session_id_size,
                                   bool has_additional_usable_key,
                                   const KeyInformation* keys_info,
                                   uint32_t keys_info_count) = 0;

  virtual void OnExpirationChange(const char* session_id,
                                  uint32_t session_id_size,
                                  Time new_expiry_time) = 0;

  virtual void OnSessionClosed(const char* session_id,
                               uint32_t session_id_size) = 0;

  virtual void SendPlatformChallenge(const char* service_id,
                                     uint32_t service_id_size,
                                     const char* challenge,
                                     uint32_t challenge_size) = 0;

  virtual void EnableOutputProtection(uint32_t desired_protection_mask) = 0;

  virtual void QueryOutputProtectionStatus() = 0;

  virtual void OnDeferredInitializationDone(StreamType stream_type,
                                            Status decoder_status) = 0;

  virtual FileIO* CreateFileIO(FileIOClient* client) = 0;

  virtual CdmProxy* RequestCdmProxy(CdmProxyClient* client) = 0;

  virtual void RequestStorageId(uint32_t version) = 0;

 protected:
  Host_11() {}
  virtual ~Host_11() {}
};

}

#endif
