namespace Grayjay.ClientServer.Payment
{
    public class Constants
    {
        public static List<CountryDescriptor> COUNTRIES = new List<CountryDescriptor>()
        {
            new CountryDescriptor("AE", "United Arab Emirates", "الإمارات العربية المتحدة", "aed", "ae"),
            new CountryDescriptor("AG", "Antigua & Barbuda", "Antigua & Barbuda", "xcd", "ag"),
            new CountryDescriptor("AL", "Albania", "Shqipëri", "all", "al"),
            new CountryDescriptor("AM", "Armenia", "Հայաստան", "amd", "am"),
            new CountryDescriptor("AO", "Angola", "Angóla", "aoa", "ao"),
            new CountryDescriptor("AR", "Argentina", "Argentina", "ars", "ar"),
            new CountryDescriptor("AT", "Austria", "Österreich", "eur", "at"),
            new CountryDescriptor("AU", "Australia", "Australia", "aud", "au"),
            new CountryDescriptor("AZ", "Azerbaijan", "Азәрбајҹан", "azn", "az"),
            new CountryDescriptor("BA", "Bosnia & Herzegovina", "Босна и Херцеговина", "bam", "ba"),
            new CountryDescriptor("BD", "Bangladesh", "বাংলাদেশ", "bdt", "bd"),
            new CountryDescriptor("BE", "Belgium", "Belgien", "eur", "be"),
            new CountryDescriptor("BG", "Bulgaria", "България", "bgn", "bg"),
            new CountryDescriptor("BH", "Bahrain", "البحرين", "bhd", "bh"),
            new CountryDescriptor("BJ", "Benin", "Bénin", "xof", "bj"),
            new CountryDescriptor("BN", "Brunei", "Brunei", "bnd", "bn"),
            new CountryDescriptor("BO", "Bolivia", "Bolivia", "bob", "bo"),
            new CountryDescriptor("BR", "Brazil", "Brasil", "brl", "br"),
            new CountryDescriptor("BS", "Bahamas", "Bahamas", "bsd", "bs"),
            new CountryDescriptor("BT", "Bhutan", "འབྲུག", "btn", "bt"),
            new CountryDescriptor("BW", "Botswana", "Botswana", "bwp", "bw"),
            new CountryDescriptor("CA", "Canada", "Canada", "cad", "ca"),
            new CountryDescriptor("CH", "Switzerland", "Schweiz", "chf", "ch"),
            new CountryDescriptor("CI", "Côte d’Ivoire", "Côte d’Ivoire", "xof", "ci"),
            new CountryDescriptor("CL", "Chile", "Chile", "clp", "cl"),
            new CountryDescriptor("CO", "Colombia", "Colombia", "cop", "co"),
            new CountryDescriptor("CR", "Costa Rica", "Costa Rica", "crc", "cr"),
            new CountryDescriptor("CY", "Cyprus", "Κύπρος", "eur", "cy"),
            new CountryDescriptor("CZ", "Czechia", "Česko", "czk", "cz"),
            new CountryDescriptor("DE", "Germany", "Deutschland", "eur", "de"),
            new CountryDescriptor("DK", "Denmark", "Danmark", "dkk", "dk"),
            new CountryDescriptor("DO", "Dominican Republic", "República Dominicana", "dop", "do"),
            new CountryDescriptor("DZ", "Algeria", "الجزائر", "dzd", "dz"),
            new CountryDescriptor("EC", "Ecuador", "Ecuador", "usd", "ec"),
            new CountryDescriptor("EE", "Estonia", "Eesti", "eur", "ee"),
            new CountryDescriptor("EG", "Egypt", "مصر", "egp", "eg"),
            new CountryDescriptor("ES", "Spain", "España", "eur", "es"),
            new CountryDescriptor("ET", "Ethiopia", "ኢትዮጵያ", "etb", "et"),
            new CountryDescriptor("FI", "Finland", "Finland", "eur", "fi"),
            new CountryDescriptor("FR", "France", "Frañs", "eur", "fr"),
            new CountryDescriptor("GA", "Gabon", "Gabon", "xaf", "ga"),
            new CountryDescriptor("GB", "United Kingdom", "United Kingdom", "gbp", "gb"),
            new CountryDescriptor("GH", "Ghana", "Gaana", "ghs", "gh"),
            new CountryDescriptor("GI", "Gibraltar", "Gibraltar", "gbp", "gi"),
            new CountryDescriptor("GM", "Gambia", "Gambia", "gmd", "gm"),
            new CountryDescriptor("GR", "Greece", "Ελλάδα", "eur", "gr"),
            new CountryDescriptor("GT", "Guatemala", "Guatemala", "gtq", "gt"),
            new CountryDescriptor("GY", "Guyana", "Guyana", "gyd", "gy"),
            new CountryDescriptor("HK", "Hong Kong SAR China", "Hong Kong SAR China", "hkd", "hk"),
            new CountryDescriptor("HR", "Croatia", "Hrvatska", "eur", "hr"),
            new CountryDescriptor("HU", "Hungary", "Magyarország", "huf", "hu"),
            new CountryDescriptor("ID", "Indonesia", "Indonesia", "idr", "id"),
            new CountryDescriptor("IE", "Ireland", "Éire", "eur", "ie"),
            new CountryDescriptor("IL", "Israel", "إسرائيل", "ils", "il"),
            new CountryDescriptor("IN", "India", "ভাৰত", "inr", "in"),
            new CountryDescriptor("IS", "Iceland", "Ísland", "eur", "is"),
            new CountryDescriptor("IT", "Italy", "Itàlia", "eur", "it"),
            new CountryDescriptor("JM", "Jamaica", "Jamaica", "jmd", "jm"),
            new CountryDescriptor("JO", "Jordan", "الأردن", "jod", "jo"),
            new CountryDescriptor("JP", "Japan", "日本", "jpy", "jp"),
            new CountryDescriptor("KE", "Kenya", "Kenya", "kes", "ke"),
            new CountryDescriptor("KH", "Cambodia", "កម្ពុជា", "khr", "kh"),
            new CountryDescriptor("KR", "South Korea", "대한민국", "krw", "kr"),
            new CountryDescriptor("KW", "Kuwait", "الكويت", "kwd", "kw"),
            new CountryDescriptor("KZ", "Kazakhstan", "Қазақстан", "kzt", "kz"),
            new CountryDescriptor("LA", "Laos", "ລາວ", "lak", "la"),
            new CountryDescriptor("LC", "St. Lucia", "St. Lucia", "xcd", "lc"),
            new CountryDescriptor("LI", "Liechtenstein", "Liechtenstein", "chf", "li"),
            new CountryDescriptor("LK", "Sri Lanka", "ශ්‍රී ලංකාව", "lkr", "lk"),
            new CountryDescriptor("LT", "Lithuania", "Lietuva", "eur", "lt"),
            new CountryDescriptor("LU", "Luxembourg", "Luxemburg", "eur", "lu"),
            new CountryDescriptor("LV", "Latvia", "Latvija", "eur", "lv"),
            new CountryDescriptor("MA", "Morocco", "المغرب", "mad", "ma"),
            new CountryDescriptor("MC", "Monaco", "Monaco", "eur", "mc"),
            new CountryDescriptor("MD", "Moldova", "Republica Moldova", "mdl", "md"),
            new CountryDescriptor("MG", "Madagascar", "Madagascar", "mga", "mg"),
            new CountryDescriptor("MK", "North Macedonia", "Северна Македонија", "mkd", "mk"),
            new CountryDescriptor("MN", "Mongolia", "Монгол", "mnt", "mn"),
            new CountryDescriptor("MO", "Macao SAR China", "Macao SAR China", "mop", "mo"),
            new CountryDescriptor("MT", "Malta", "Malta", "eur", "mt"),
            new CountryDescriptor("MU", "Mauritius", "Mauritius", "mur", "mu"),
            new CountryDescriptor("MX", "Mexico", "México", "mxn", "mx"),
            new CountryDescriptor("MY", "Malaysia", "Malaysia", "myr", "my"),
            new CountryDescriptor("MZ", "Mozambique", "Umozambiki", "mzn", "mz"),
            new CountryDescriptor("NA", "Namibia", "Namibië", "nad", "na"),
            new CountryDescriptor("NE", "Niger", "Nižer", "xof", "ne"),
            new CountryDescriptor("NG", "Nigeria", "Nigeria", "ngn", "ng"),
            new CountryDescriptor("NL", "Netherlands", "Netherlands", "eur", "nl"),
            new CountryDescriptor("NO", "Norway", "Norge", "nok", "no"),
            new CountryDescriptor("NZ", "New Zealand", "New Zealand", "nzd", "nz"),
            new CountryDescriptor("OM", "Oman", "عُمان", "omr", "om"),
            new CountryDescriptor("PA", "Panama", "Panamá", "usd", "pa"),
            new CountryDescriptor("PE", "Peru", "Perú", "pen", "pe"),
            new CountryDescriptor("PH", "Philippines", "Pilipinas", "php", "ph"),
            new CountryDescriptor("PK", "Pakistan", "Pakistan", "pkr", "pk"),
            new CountryDescriptor("PL", "Poland", "Polska", "pln", "pl"),
            new CountryDescriptor("PT", "Portugal", "Portugal", "eur", "pt"),
            new CountryDescriptor("PY", "Paraguay", "Paraguay", "pyg", "py"),
            new CountryDescriptor("QA", "Qatar", "قطر", "qar", "qa"),
            new CountryDescriptor("RO", "Romania", "România", "ron", "ro"),
            new CountryDescriptor("RS", "Serbia", "Србија", "rsd", "rs"),
            new CountryDescriptor("RU", "Russia", "Российская Федерация", "rub", "ru"),
            new CountryDescriptor("RW", "Rwanda", "Rwanda", "rwf", "rw"),
            new CountryDescriptor("SA", "Saudi Arabia", "المملكة العربية السعودية", "sar", "sa"),
            new CountryDescriptor("SE", "Sweden", "Sweden", "sek", "se"),
            new CountryDescriptor("SG", "Singapore", "Singapore", "sgd", "sg"),
            new CountryDescriptor("SI", "Slovenia", "Slovenia", "eur", "si"),
            new CountryDescriptor("SK", "Slovakia", "Slovensko", "eur", "sk"),
            new CountryDescriptor("SM", "San Marino", "San Marino", "eur", "sm"),
            new CountryDescriptor("SN", "Senegal", "Senegal", "xof", "sn"),
            new CountryDescriptor("SV", "El Salvador", "El Salvador", "usd", "sv"),
            new CountryDescriptor("TH", "Thailand", "ไทย", "thb", "th"),
            new CountryDescriptor("TN", "Tunisia", "تونس", "tnd", "tn"),
            new CountryDescriptor("TR", "Turkey", "Türkiye", "try", "tr"),
            new CountryDescriptor("TT", "Trinidad & Tobago", "Trinidad & Tobago", "ttd", "tt"),
            new CountryDescriptor("TW", "Taiwan", "台灣", "twd", "tw"),
            new CountryDescriptor("TZ", "Tanzania", "Tadhania", "tzs", "tz"),
            new CountryDescriptor("US", "United States", "United States", "usd", "us"),
            new CountryDescriptor("UY", "Uruguay", "Uruguay", "uyu", "uy"),
            new CountryDescriptor("UZ", "Uzbekistan", "Ўзбекистон", "uzs", "uz"),
            new CountryDescriptor("VN", "Vietnam", "Việt Nam", "vnd", "vn"),
            new CountryDescriptor("ZA", "South Africa", "Suid-Afrika", "zar", "za")
        };
        public static CountryDescriptor US { get; } = COUNTRIES.FirstOrDefault(x => x.ID == "US");

        public static List<CurrencyDescriptor> CURRENCIES = new List<CurrencyDescriptor>()
        {
            new CurrencyDescriptor("aed", "United Arab Emirates Dirham", "درهم إماراتي", "د.إ.‏", "ae"), //AE
            new CurrencyDescriptor("xcd", "East Caribbean Dollar", "East Caribbean Dollar", "$", "ag"), //AG, LC
            new CurrencyDescriptor("all", "Albanian Lek", "Leku shqiptar", "Lekë", "al"), //AL
            new CurrencyDescriptor("amd", "Armenian Dram", "հայկական դրամ", "֏", "am"), //AM
            new CurrencyDescriptor("aoa", "Angolan Kwanza", "Kwanza ya Angóla", "Kz", "ao"), //AO
            new CurrencyDescriptor("ars", "Argentine Peso", "peso argentino", "$", "ar"), //AR
            new CurrencyDescriptor("eur", "Euro", "Euro", "€", "eu"), //AT, BE, CY, DE, EE, ES, FI, FR, GR, HR, IE, IS, IT, LT, LU, LV, MC, MT, NL, PT, SI, SK, SM
            new CurrencyDescriptor("aud", "Australian Dollar", "Australian Dollar", "$", "au"), //AU
            new CurrencyDescriptor("azn", "Azerbaijani Manat", "AZN", "₼", "az"), //AZ
            new CurrencyDescriptor("bam", "Bosnia-Herzegovina Convertible Mark", "Конвертибилна марка", "КМ", "ba"), //BA
            new CurrencyDescriptor("bdt", "Bangladeshi Taka", "বাংলাদেশী টাকা", "৳", "bd"), //BD
            new CurrencyDescriptor("bgn", "Bulgarian Lev", "Български лев", "лв.", "bg"), //BG
            new CurrencyDescriptor("bhd", "Bahraini Dinar", "دينار بحريني", "د.ب.‏", "bh"), //BH
            new CurrencyDescriptor("xof", "West African CFA Franc", "franc CFA (BCEAO)", "F CFA", "bj"), //BJ, CI, NE, SN
            new CurrencyDescriptor("bnd", "Brunei Dollar", "Dolar Brunei", "$", "bn"), //BN
            new CurrencyDescriptor("bob", "Bolivian Boliviano", "boliviano", "Bs", "bo"), //BO
            new CurrencyDescriptor("brl", "Brazilian Real", "real brasileño", "R$", "br"), //BR
            new CurrencyDescriptor("bsd", "Bahamian Dollar", "Bahamian Dollar", "$", "bs"), //BS
            new CurrencyDescriptor("btn", "Bhutanese Ngultrum", "དངུལ་ཀྲམ", "Nu.", "bt"), //BT
            new CurrencyDescriptor("bwp", "Botswanan Pula", "Botswanan Pula", "P", "bw"), //BW
            new CurrencyDescriptor("cad", "Canadian Dollar", "Canadian Dollar", "$", "ca"), //CA
            new CurrencyDescriptor("chf", "Swiss Franc", "Schweizer Franken", "CHF", "ch"), //CH, LI
            new CurrencyDescriptor("clp", "Chilean Peso", "Peso chileno", "$", "cl"), //CL
            new CurrencyDescriptor("cop", "Colombian Peso", "peso colombiano", "$", "co"), //CO
            new CurrencyDescriptor("crc", "Costa Rican Colón", "colón costarricense", "₡", "cr"), //CR
            new CurrencyDescriptor("czk", "Czech Koruna", "česká koruna", "Kč", "cz"), //CZ
            new CurrencyDescriptor("dkk", "Danish Krone", "dansk krone", "kr.", "dk"), //DK
            new CurrencyDescriptor("dop", "Dominican Peso", "peso dominicano", "RD$", "do"), //DO
            new CurrencyDescriptor("dzd", "Algerian Dinar", "دينار جزائري", "د.ج.‏", "dz"), //DZ
            new CurrencyDescriptor("usd", "United States Dollar", "dólar estadounidense", "$", "us"), //EC, PA, SV, US
            new CurrencyDescriptor("egp", "Egyptian Pound", "جنيه مصري", "ج.م.‏", "eg"), //EG
            new CurrencyDescriptor("etb", "Ethiopian Birr", "የኢትዮጵያ ብር", "ብር", "et"), //ET
            new CurrencyDescriptor("xaf", "Central African CFA Franc", "franc CFA (BEAC)", "FCFA", "ga"), //GA
            new CurrencyDescriptor("gbp", "British Pound", "Punt Prydain", "£", "gb"), //GB, GI
            new CurrencyDescriptor("ghs", "Ghanaian Cedi", "Ghana Sidi", "GH₵", "gh"), //GH
            new CurrencyDescriptor("gmd", "Gambian Dalasi", "Gambian Dalasi", "D", "gm"), //GM
            new CurrencyDescriptor("gtq", "Guatemalan Quetzal", "quetzal", "Q", "gt"), //GT
            new CurrencyDescriptor("gyd", "Guyanaese Dollar", "Guyanaese Dollar", "$", "gy"), //GY
            new CurrencyDescriptor("hkd", "Hong Kong Dollar", "Hong Kong Dollar", "HK$", "hk"), //HK
            new CurrencyDescriptor("huf", "Hungarian Forint", "magyar forint", "Ft", "hu"), //HU
            new CurrencyDescriptor("idr", "Indonesian Rupiah", "Rupiah Indonesia", "Rp", "id"), //ID
            new CurrencyDescriptor("ils", "Israeli New Shekel", "شيكل إسرائيلي جديد", "₪", "il"), //IL
            new CurrencyDescriptor("inr", "Indian Rupee", "ভাৰতীয় ৰুপী", "₹", "in"), //IN
            new CurrencyDescriptor("jmd", "Jamaican Dollar", "Jamaican Dollar", "$", "jm"), //JM
            new CurrencyDescriptor("jod", "Jordanian Dinar", "دينار أردني", "د.أ.‏", "jo"), //JO
            new CurrencyDescriptor("jpy", "Japanese Yen", "日本円", "￥", "jp"), //JP
            new CurrencyDescriptor("kes", "Kenyan Shilling", "Shilingi ya Kenya", "Ksh", "ke"), //KE
            new CurrencyDescriptor("khr", "Cambodian Riel", "រៀល​កម្ពុជា", "៛", "kh"), //KH
            new CurrencyDescriptor("krw", "South Korean Won", "대한민국 원", "₩", "kr"), //KR
            new CurrencyDescriptor("kwd", "Kuwaiti Dinar", "دينار كويتي", "د.ك.‏", "kw"), //KW
            new CurrencyDescriptor("kzt", "Kazakhstani Tenge", "Қазақстан теңгесі", "₸", "kz"), //KZ
            new CurrencyDescriptor("lak", "Laotian Kip", "ລາວ ກີບ", "₭", "la"), //LA
            new CurrencyDescriptor("lkr", "Sri Lankan Rupee", "ශ්‍රී ලංකා රුපියල", "රු.", "lk"), //LK
            new CurrencyDescriptor("mad", "Moroccan Dirham", "درهم مغربي", "د.م.‏", "ma"), //MA
            new CurrencyDescriptor("mdl", "Moldovan Leu", "leu moldovenesc", "L", "md"), //MD
            new CurrencyDescriptor("mga", "Malagasy Ariary", "Malagasy Ariary", "Ar", "mg"), //MG
            new CurrencyDescriptor("mkd", "Macedonian Denar", "Македонски денар", "ден.", "mk"), //MK
            new CurrencyDescriptor("mnt", "Mongolian Tugrik", "Монгол төгрөг", "₮", "mn"), //MN
            new CurrencyDescriptor("mop", "Macanese Pataca", "Macanese Pataca", "MOP$", "mo"), //MO
            new CurrencyDescriptor("mur", "Mauritian Rupee", "Mauritian Rupee", "Rs", "mu"), //MU
            new CurrencyDescriptor("mxn", "Mexican Peso", "peso mexicano", "$", "mx"), //MX
            new CurrencyDescriptor("myr", "Malaysian Ringgit", "Malaysian Ringgit", "RM", "my"), //MY
            new CurrencyDescriptor("mzn", "Mozambican Metical", "MZN", "MTn", "mz"), //MZ
            new CurrencyDescriptor("nad", "Namibian Dollar", "Namibiese dollar", "$", "na"), //NA
            new CurrencyDescriptor("ngn", "Nigerian Naira", "Nigerian Naira", "₦", "ng"), //NG
            new CurrencyDescriptor("nok", "Norwegian Krone", "norske kroner", "kr", "no"), //NO
            new CurrencyDescriptor("nzd", "New Zealand Dollar", "New Zealand Dollar", "$", "nz"), //NZ
            new CurrencyDescriptor("omr", "Omani Rial", "ريال عماني", "ر.ع.‏", "om"), //OM
            new CurrencyDescriptor("pen", "Peruvian Sol", "sol peruano", "S/", "pe"), //PE
            new CurrencyDescriptor("php", "Philippine Peso", "Philippine Piso", "₱", "ph"), //PH
            new CurrencyDescriptor("pkr", "Pakistani Rupee", "Pakistani Rupee", "Rs", "pk"), //PK
            new CurrencyDescriptor("pln", "Polish Zloty", "złoty polski", "zł", "pl"), //PL
            new CurrencyDescriptor("pyg", "Paraguayan Guarani", "guaraní paraguayo", "Gs.", "py"), //PY
            new CurrencyDescriptor("qar", "Qatari Rial", "ريال قطري", "ر.ق.‏", "qa"), //QA
            new CurrencyDescriptor("ron", "Romanian Leu", "leu românesc", "RON", "ro"), //RO
            new CurrencyDescriptor("rsd", "Serbian Dinar", "српски динар", "RSD", "rs"), //RS
            new CurrencyDescriptor("rub", "Russian Rubble", "Российский рубль", "RUB", "ru"), //RU
            new CurrencyDescriptor("rwf", "Rwandan Franc", "Rwandan Franc", "RF", "rw"), //RW
            new CurrencyDescriptor("sar", "Saudi Riyal", "ريال سعودي", "ر.س.‏", "sa"), //SA
            new CurrencyDescriptor("sek", "Swedish Krona", "Swedish Krona", "kr", "se"), //SE
            new CurrencyDescriptor("sgd", "Singapore Dollar", "Singapore Dollar", "$", "sg"), //SG
            new CurrencyDescriptor("thb", "Thai Baht", "บาท", "฿", "th"), //TH
            new CurrencyDescriptor("tnd", "Tunisian Dinar", "دينار تونسي", "د.ت.‏", "tn"), //TN
            new CurrencyDescriptor("try", "Turkish Lira", "TRY", "₺", "tr"), //TR
            new CurrencyDescriptor("ttd", "Trinidad & Tobago Dollar", "Trinidad & Tobago Dollar", "$", "tt"), //TT
            new CurrencyDescriptor("twd", "New Taiwan Dollar", "新台幣", "$", "tw"), //TW
            new CurrencyDescriptor("tzs", "Tanzanian Shilling", "shilingi ya Tandhania", "TSh", "tz"), //TZ
            new CurrencyDescriptor("uyu", "Uruguayan Peso", "peso uruguayo", "$", "uy"), //UY
            new CurrencyDescriptor("uzs", "Uzbekistani Som", "Ўзбекистон сўм", "сўм", "uz"), //UZ
            new CurrencyDescriptor("vnd", "Vietnamese Dong", "Đồng Việt Nam", "₫", "vn"), //VN
            new CurrencyDescriptor("zar", "South African Rand", "Suid-Afrikaanse rand", "R", "za"), //ZA
        };
        public static CurrencyDescriptor USD { get; } = CURRENCIES.FirstOrDefault(x => x.ID == "usd");
    }

    public class CountryDescriptor
    {
        public string ID { get; set; }
        public string NameEnglish { get; set; }
        public string NameNative { get; set; }
        public string DefaultCurrencyID { get; set; }
        public string Flag { get; set; }

        public CountryDescriptor(string id, string nameEnglish, string nameNative, string currencyId, string flag)
        {
            ID = id;
            NameEnglish = nameEnglish;
            NameNative = nameNative;
            DefaultCurrencyID = currencyId;
            Flag = flag;
        }
    }
    public class CurrencyDescriptor
    {
        public string ID { get; set; }
        public string NameEnglish { get; set; }
        public string NameNative { get; set; }
        public string Symbol { get; set; }
        public string Flag { get; set; }
        public CurrencyDescriptor(string id, string nameEnglish, string nameNative, string symbol, string flag)
        {
            ID = id;
            NameEnglish = nameEnglish;
            NameNative = nameNative;
            Symbol = symbol;
            Flag = flag;
        }
    }
}
