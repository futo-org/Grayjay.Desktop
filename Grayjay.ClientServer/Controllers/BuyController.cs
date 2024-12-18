using Grayjay.ClientServer.Crypto;
using Grayjay.ClientServer.Payment;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System.Globalization;
using System.Net;
using System.Runtime.ConstrainedExecution;
using System.Text.Json.Nodes;
using System.Threading;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class BuyController : ControllerBase
    {
        private const string URL_PRICES = "https://spayment.grayjay.app/api/v1/payment/prices";
        private const string URL_ACTIVATE = "https://spayment.grayjay.app/api/v1/activate/";
        private static string _price = null;
        
        private static StringStore licenseStore = new StringStore("license", null);
        private static LicenseValidator validator = new LicenseValidator("MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAzJqqETLa42xw4AfbNOLQolMdMiGgg8DAC4RXEcH4/gytLhaqp1XsjiiMkADi1C7sDtGj6kOuAuQkqXQKpZ2dJSZsO+GPyop6DmgfAM6MQgOgFUpwsb3Lt3SvskJcls8MeOC+jg+GjjcuJI8qOfYevj4/7wAOpqzAwocTYnJivlK5nrC+qNtUC2HZX93OVu69aU5yvA1SQe9GiiU7vBld+CbzHxTcABCK/THu/BpLtGx0M7W3HNMKK1Z79dopCL9ZZWbWdkGDY8Zf39Gn/WVrs5elBvPzU+AfNYty77vx2r+sKgyohlbz4KVYpnw8HfawKcwuRE/GUyD3F2hUcXy8dQIDAQAB");

        private static string[] _currencyDecimal2 = new string[] { " ISK", "HUF", "TWD", "UGX" };
        private static string[] _currencyDecimal0 = new string[] { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "VND", "VUV", "XAF", "XOF", "XPF" };
        private static string[] _currencyDecimal3 = new string[] { "BHD", "JOD", "KWD", "OMR", "TND" };

        public static Action<bool> OnLicenseStatusChanged;

        static BuyController()
        {
            OnLicenseStatusChanged += (changed) =>
            {
                StateWebsocket.LicenseStatusChanged(changed);
            };
        }


        [HttpGet]
        public void OpenBuy()
        {
            OSHelper.OpenUrl("https://pay.futo.org/api/PaymentPortal?product=grayjay");
        }

        [HttpGet]
        public string GetPrice()
        {
            if (_price != null)
                return _price;
            using(WebClient client = new WebClient())
            {
                string country = GetCountryFromIP(client);
                string json = client.DownloadString($"{URL_PRICES}?productId=grayjay");
                var curs = JsonConvert.DeserializeObject<Dictionary<string, long>>(json);

                CountryDescriptor countryDescriptor = Payment.Constants.COUNTRIES.FirstOrDefault(x => x.ID.Equals(country, StringComparison.InvariantCultureIgnoreCase));
                CurrencyDescriptor currency = countryDescriptor != null ? Payment.Constants.CURRENCIES.FirstOrDefault(x => x.ID == countryDescriptor?.DefaultCurrencyID) ?? Payment.Constants.USD : Payment.Constants.USD;
                string idLower = currency.ID.ToLower();
                long val = 0;
                if (curs.ContainsKey(idLower))
                    val = curs[idLower];
                else if (curs.ContainsKey("usd"))
                {
                    currency = Payment.Constants.USD;
                    val = curs["usd"];
                }
                else
                    return "Unknown";

                int decimalPlaces = 2;
                if (_currencyDecimal0.Contains(currency.ID))
                    decimalPlaces = 0;
                else if (_currencyDecimal3.Contains(currency.ID))
                    decimalPlaces = 3;
                double valueAdjusted = val / Math.Pow((double)10, (double)decimalPlaces);
                switch(decimalPlaces)
                {
                    case 0:
                        _price = currency.Symbol + valueAdjusted;
                        break;
                    case 3:
                        _price = currency.Symbol + valueAdjusted.ToString("0.###");
                        break;
                    default:
                        _price = currency.Symbol + valueAdjusted.ToString("0.##");
                        break;
                }
                return _price;
            }
        }

        [HttpGet]
        public bool DidPurchase()
        {
            return licenseStore.Value != null;
        }

        [HttpPost]
        public bool SetActivation([FromBody]string[] act)
        {
                string licenseKey = act[0];
                string activationKey = act[1];

                if(validator.Validate(licenseKey, activationKey))
                {
                    licenseStore.Save(EncryptionProvider.Instance.Encrypt(JsonConvert.SerializeObject(new string[] { licenseKey, activationKey })));
                    OnLicenseStatusChanged?.Invoke(true);
                    return true;
                }
                return false;
        }
        [HttpPost]
        public bool SetLicenseUrl([FromBody] string licenseUrl)
        {
            try
            {

                var urlToUse = (licenseUrl.StartsWith("grayjay://") ? licenseUrl.Substring("grayjay://".Length) : licenseUrl);

                if (urlToUse.StartsWith("license/"))
                    urlToUse = urlToUse.Substring("license/".Length);

                string[] parts = urlToUse.Split("/");
                if (parts.Length != 2)
                    return false;

                var licenseKey = parts[0];
                var activationKey = parts[1];
                return SetActivation(new string[] { licenseKey, activationKey });
            }
            catch (Exception ex)
            {
                Logger.e(nameof(BuyController), "Failed to set license due to: " + ex.Message, ex);
                return false;
            }
            }
        [HttpPost]
        public bool SetLicense([FromBody] string license)
        {
            try
            {
                string licenseKey = license;
                using (WebClient client = new WebClient())
                {
                    string activate = client.DownloadString(URL_ACTIVATE + licenseKey);
                    return SetActivation(new string[] { license, activate });
                }
            }
            catch (Exception ex)
            {
                Logger.e(nameof(BuyController), "Failed to set license due to: " + ex.Message, ex);
                return false;
            }
        }
        [HttpPost]
        public void ClearLicense()
        {
            licenseStore.Save(null);
            OnLicenseStatusChanged?.Invoke(false);
        }

        private static string GetCountryFromIP(WebClient client)
        {
            try
            {
                var urlString = "https://freeipapi.com/api/json";

                string json = client.DownloadString(urlString);

                var obj = JsonObject.Parse(json).AsObject();
                if (obj.ContainsKey("countryCode"))
                    return ((string?)obj["countryCode"]);
                return null;
            }
            catch(Exception ex)
            {
                return null;
            }
        }
    }
}
