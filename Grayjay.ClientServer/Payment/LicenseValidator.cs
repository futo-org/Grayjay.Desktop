using System;
using System.Security.Cryptography;
using System.Text;

namespace Grayjay.ClientServer.Payment
{
    public class LicenseValidator
    {
        private readonly RSA _publicPaymentKey;

        public LicenseValidator(string publicKey)
        {
            byte[] keyBytes = Convert.FromBase64String(publicKey);
            _publicPaymentKey = RSA.Create();

            try
            {
                _publicPaymentKey.ImportSubjectPublicKeyInfo(keyBytes, out _);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("Invalid public key format.", ex);
            }
        }

        public bool Validate(string licenseKey, string activationKey)
        {
            byte[] data = Encoding.UTF8.GetBytes(licenseKey);
            byte[] signature = DecodeUrlSafeBase64(activationKey);
            return _publicPaymentKey.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        private static byte[] DecodeUrlSafeBase64(string urlSafeBase64)
        {
            string base64 = urlSafeBase64
                .Replace('-', '+')
                .Replace('_', '/');

            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            return Convert.FromBase64String(base64);
        }
    }
}
