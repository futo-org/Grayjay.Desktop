using Grayjay.ClientServer.Sync;
using Grayjay.Engine.Serializers;
using Microsoft.Extensions.ObjectPool;
using System.Diagnostics.Contracts;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Grayjay.ClientServer.SubsExchange
{
    public class SubsExchangeClient
    {
        private string _server;
        private string _privateKey;
        private string _publicKey;


        public SubsExchangeClient(string server, string privateKey)
        {
            _server = server;
            _privateKey = privateKey;
            _publicKey = ExtractPublicKey(_privateKey);
        }


        //Endpoints

        //Endpoint: Contract
        public ExchangeContract RequestContract(params ChannelRequest[] channels)
        {
            string data = POST("/api/Channel/Contract", JsonSerializer.Serialize(channels), "application/json");
            ExchangeContract contract = GJsonSerializer.AndroidCompatible.DeserializeObj<ExchangeContract>(data);
            return contract;
        }
        public async Task<ExchangeContract> RequestContractAsync(params ChannelRequest[] channels)
        {
            string data = await POSTAsync("/api/Channel/Contract", JsonSerializer.Serialize(channels), "application/json");
            ExchangeContract contract = GJsonSerializer.AndroidCompatible.DeserializeObj<ExchangeContract>(data);
            return contract;
        }

        //Endpoint: Resolve
        public ChannelResult[] ResolveContract(ExchangeContract contract, ChannelResolve[] resolves)
        {
            var contractResolve = ConvertResolves(resolves);
            string result = POST("/api/Channel/Resolve?contractId=" + contract.ID, JsonSerializer.Serialize(contractResolve), "application/json");
            ChannelResult[] results = GJsonSerializer.AndroidCompatible.DeserializeObj<ChannelResult[]>(result);
            return results;
        }
        public async Task<ChannelResult[]> ResolveContractAsync(ExchangeContract contract, ChannelResolve[] resolves)
        {
            var contractResolve = ConvertResolves(resolves);
            string result = await POSTAsync("/api/Channel/Resolve?contractId=" + contract.ID, JsonSerializer.Serialize(contractResolve), "application/json");
            ChannelResult[] results = GJsonSerializer.AndroidCompatible.DeserializeObj<ChannelResult[]>(result);
            return results;
        }



        private ExchangeContractResolve ConvertResolves(ChannelResolve[] resolves)
        {
            string data = GJsonSerializer.Serialize(resolves);
            string signature = CreateSignature(data, _privateKey);

            ExchangeContractResolve contractResolve = new ExchangeContractResolve()
            {
                PublicKey = _publicKey,
                Signature = signature,
                Data = data,
            };
            return contractResolve;
        }


        //IO
        private async Task<string> POSTAsync(string query, string body, string contentType)
        {
            using (HttpClient client = new HttpClient())
            {
                var data = await client.PostAsync(_server + query, new StringContent(body, MediaTypeHeaderValue.Parse(contentType)));

                return await data.Content.ReadAsStringAsync();
            }
        }
        private string POST(string query, string body, string contentType)
        {
            using(WebClient client = new WebClient())
            {
                client.Headers.Add("Content-Type", contentType);
                string data = client.UploadString(_server + query, body);
                return data;
            }
        }


        //Crypto
        public static string CreatePrivateKey()
        {
            using (RSA rsa = RSA.Create())
            {
                return rsa.ExportPkcs8PrivateKeyPem();
            }
        }
        public static string ExtractPublicKey(string privateKey)
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportFromPem(privateKey);
                return rsa.ExportRSAPublicKeyPem();
            }
        }

        public static string CreateSignature(string data, string privateKey)
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportFromPem(privateKey);

                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                byte[] sigBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                return Convert.ToBase64String(sigBytes);
            }
        }
    }
}
