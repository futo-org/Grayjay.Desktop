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
            byte[] keyBytes = publicKey.DecodeBase64();
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
            byte[] signature = activationKey.DecodeBase64Url();
            return _publicPaymentKey.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }
}
