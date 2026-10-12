using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Grayjay.Desktop.CEF.LinuxCdm;

internal static class Crx3
{
    private static readonly byte[] WidevineKeyHash = Convert.FromHexString("E8CECF4206D093496DD989E14104864A8FBD8612B9589BFB4FBB1BA9D38537EF");

    internal static ReadOnlyMemory<byte> VerifyWidevine(byte[] file)
    {
        if (file.Length < 12 || !file.AsSpan(0, 4).SequenceEqual("Cr24"u8) || BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)) != 3)
            throw new InvalidDataException("Widevine package is not CRX3.");
        uint headerLength = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8));
        if (headerLength > 1024 * 1024 || 12L + headerLength >= file.Length)
            throw new InvalidDataException("Invalid Widevine package header.");
        var fields = Fields(file.AsSpan(12, (int)headerLength));
        byte[] signedHeader = Single(fields, 10000);
        byte[] id = Single(Fields(signedHeader), 1);
        if (!id.AsSpan().SequenceEqual(WidevineKeyHash.AsSpan(0, 16)))
            throw new InvalidDataException("Widevine package has the wrong component identity.");
        int archiveOffset = 12 + (int)headerLength;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(Encoding.UTF8.GetBytes("CRX3 SignedData\0"));
        byte[] size = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)signedHeader.Length);
        digest.AppendData(size); digest.AppendData(signedHeader); digest.AppendData(file.AsSpan(archiveOffset));
        byte[] hash = digest.GetHashAndReset();
        foreach (var proof in fields.Where(x => x.Number is 2 or 3))
        {
            var contents = Fields(proof.Value);
            byte[] key = Single(contents, 1), signature = Single(contents, 2);
            if (!SHA256.HashData(key).AsSpan().SequenceEqual(WidevineKeyHash)) continue;
            bool valid;
            if (proof.Number == 2)
            {
                using var rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(key, out int consumed);
                valid = consumed == key.Length && rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            else
            {
                using var ec = ECDsa.Create(); ec.ImportSubjectPublicKeyInfo(key, out int consumed);
                valid = consumed == key.Length && ec.VerifyHash(hash, signature, DSASignatureFormat.Rfc3279DerSequence);
            }
            if (valid) return file.AsMemory(archiveOffset);
        }
        throw new InvalidDataException("Widevine package signature verification failed.");
    }
    private sealed record Field(int Number, byte[] Value);
    private static byte[] Single(List<Field> fields, int number)
    {
        var found = fields.Where(x => x.Number == number).ToArray();
        return found.Length == 1 ? found[0].Value : throw new InvalidDataException("Invalid CRX3 signed header.");
    }
    private static List<Field> Fields(ReadOnlySpan<byte> data)
    {
        var result = new List<Field>(); int at = 0;
        while (at < data.Length)
        {
            ulong tag = Varint(data, ref at);
            int number = checked((int)(tag >> 3));
            if (number == 0) throw new InvalidDataException("Invalid protobuf field.");
            switch (tag & 7)
            {
                case 0: Varint(data, ref at); break;
                case 1: at = checked(at + 8); break;
                case 2:
                    int length = checked((int)Varint(data, ref at));
                    if (length > data.Length - at) throw new InvalidDataException("Truncated CRX3 header.");
                    result.Add(new(number, data.Slice(at, length).ToArray())); at += length; break;
                case 5: at = checked(at + 4); break;
                default: throw new InvalidDataException("Unsupported CRX3 protobuf encoding.");
            }
            if (at > data.Length) throw new InvalidDataException("Truncated CRX3 header.");
        }
        return result;
    }
    private static ulong Varint(ReadOnlySpan<byte> data, ref int at)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (at >= data.Length) throw new InvalidDataException("Truncated protobuf integer.");
            byte b = data[at++];
            if (shift == 63 && b > 1) throw new InvalidDataException("Invalid protobuf integer.");
            value |= (ulong)(b & 127) << shift;
            if ((b & 128) == 0) return value;
        }
        throw new InvalidDataException("Invalid protobuf integer.");
    }
}
