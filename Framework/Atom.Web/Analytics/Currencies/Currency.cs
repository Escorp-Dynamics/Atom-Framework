#pragma warning disable IDE0046

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Atom.Text.Json;

namespace Atom.Web.Analytics;

/// <summary>
/// Данные о валюте.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Currency"/>.
/// </remarks>
/// <param name="name">Название.</param>
/// <param name="internationalName">Название (интернациональное).</param>
/// <param name="code">Цифровой код валюты.</param>
/// <param name="isoCode">Символьный код валюты (ISO 4217).</param>
/// <param name="taylorCode">Код Тэйлора.</param>
/// <param name="kkb">Код KKB.</param>
/// <param name="symbol">Символьный код (Unicode).</param>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
[JsonConverter(typeof(CurrencyJsonConverter))]
[JsonContext(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
public sealed partial class Currency
(
    string name,
    string internationalName,
    ushort code,
    string isoCode,
    string? taylorCode,
    string? kkb,
    char? symbol
) : IParsable<Currency?>, IEquatable<Currency>
{
    /// <summary>
    /// Название.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Название (интернациональное).
    /// </summary>
    public string InternationalName { get; } = internationalName;

    /// <summary>
    /// Цифровой код валюты.
    /// </summary>
    public ushort Code { get; } = code;

    /// <summary>
    /// Символьный код валюты.
    /// </summary>
    public string IsoCode { get; } = isoCode;

    /// <summary>
    /// Код Тэйлора.
    /// </summary>
    public string? TaylorCode { get; } = taylorCode;

    /// <summary>
    /// Код KKB.
    /// </summary>
    public string? KKB { get; } = kkb;

    /// <summary>
    /// Символьный код (Unicode).
    /// </summary>
    public char? Symbol { get; } = symbol;

    #region Инициализации

    private static readonly Lazy<Currency> aed = new(() => new Currency("Дирхам (ОАЭ)", "UAE Dirham", 784, "AED"), true);
    private static readonly Lazy<Currency> afn = new(() => new Currency("Афгани", "Afghani", 971, "AFN", '؋'), true);
    private static readonly Lazy<Currency> all = new(() => new Currency("Лек", "Lek", 008, "ALL"), true);
    private static readonly Lazy<Currency> amd = new(() => new Currency("Армянский драм", "Armenian Dram", 051, "AMD", '֏'), true);
    private static readonly Lazy<Currency> ang = new(() => new Currency("Нидерландский антильский гульден", "Netherlands Antillean Guilder", 532, "ANG", 'ƒ'), true);
    private static readonly Lazy<Currency> aoa = new(() => new Currency("Кванза", "Kwanza", 973, "AOA", "B59"), true);
    private static readonly Lazy<Currency> ars = new(() => new Currency("Аргентинское песо", "Argentine Peso", 032, "ARS", '$'), true);
    private static readonly Lazy<Currency> aud = new(() => new Currency("Австралийский доллар", "Australian Dollar", 036, "AUD", "B67", '$'), true);
    private static readonly Lazy<Currency> awg = new(() => new Currency("Арубанский флорин", "Aruban Florin", 533, "AWG", 'ƒ'), true);
    private static readonly Lazy<Currency> azn = new(() => new Currency("Азербайджанский манат", "Azerbaijanian Manat", 944, "AZN"), true);
    private static readonly Lazy<Currency> bam = new(() => new Currency("Конвертируемая марка", "Convertible Mark", 977, "BAM"), true);
    private static readonly Lazy<Currency> bbd = new(() => new Currency("Барбадосский доллар", "Barbados Dollar", 052, "BBD", '$'), true);
    private static readonly Lazy<Currency> bdt = new(() => new Currency("Така", "Taka", 050, "BDT", '৳'), true);
    private static readonly Lazy<Currency> bgn = new(() => new Currency("Болгарский лев", "Bulgarian Lev", 975, "BGN", "3"), true);
    private static readonly Lazy<Currency> bhd = new(() => new Currency("Бахрейнский динар", "Bahraini Dinar", 048, "BHD"), true);
    private static readonly Lazy<Currency> bif = new(() => new Currency("Бурундийский франк", "Burundi Franc", 108, "BIF"), true);
    private static readonly Lazy<Currency> bmd = new(() => new Currency("Бермудский доллар", "Bermudian Dollar", 060, "BMD", '$'), true);
    private static readonly Lazy<Currency> bnd = new(() => new Currency("Брунейский доллар", "Brunei Dollar", 096, "BND", '$'), true);
    private static readonly Lazy<Currency> bob = new(() => new Currency("Боливиано", "Boliviano", 068, "BOB"), true);
    private static readonly Lazy<Currency> bov = new(() => new Currency("Мвдол", "Mvdol", 984, "BOV"), true);
    private static readonly Lazy<Currency> brl = new(() => new Currency("Бразильский реал", "Brazilian Real", 986, "BRL", "C42"), true);
    private static readonly Lazy<Currency> bsd = new(() => new Currency("Багамский доллар", "Bahamian Dollar", 044, "BSD", '$'), true);
    private static readonly Lazy<Currency> btn = new(() => new Currency("Нгултрум", "Ngultrum", 064, "BTN"), true);
    private static readonly Lazy<Currency> bwp = new(() => new Currency("Пула", "Pula", 072, "BWP"), true);
    private static readonly Lazy<Currency> byn = new(() => new Currency("Белорусский рубль", "Belarussian Ruble", 933, "BYN"), true);
    private static readonly Lazy<Currency> bzd = new(() => new Currency("Белизский доллар", "Belize Dollar", 084, "BZD", '$'), true);
    private static readonly Lazy<Currency> cad = new(() => new Currency("Канадский доллар", "Canadian Dollar", 124, "CAD", '$'), true);
    private static readonly Lazy<Currency> cdf = new(() => new Currency("Конголезский франк", "Congolese Franc", 976, "CDF"), true);
    private static readonly Lazy<Currency> che = new(() => new Currency("WIR-евро", "WIR Euro", 947, "CHE"), true);
    private static readonly Lazy<Currency> chf = new(() => new Currency("Швейцарский франк", "Swiss Franc", 756, "CHF", "12"), true);
    private static readonly Lazy<Currency> chw = new(() => new Currency("WIR-франк", "WIR Franc", 948, "CHW"), true);
    private static readonly Lazy<Currency> clf = new(() => new Currency("Единица развития", "Unidades de Fomento", 990, "CLF"), true);
    private static readonly Lazy<Currency> clp = new(() => new Currency("Чилийское песо", "Chilean Peso", 152, "CLP", '$'), true);
    private static readonly Lazy<Currency> cny = new(() => new Currency("Юань", "Yuan Renminbi", 156, "CNY", '¥'), true);
    private static readonly Lazy<Currency> cop = new(() => new Currency("Колумбийское песо", "Colombian Peso", 170, "COP", '$'), true);
    private static readonly Lazy<Currency> cou = new(() => new Currency("Единица реальной стоимости", "Unidad de Valor Real", 970, "COU"), true);
    private static readonly Lazy<Currency> crc = new(() => new Currency("Коста-риканский колон", "Costa Rican Colon", 188, "CRC", '₡'), true);
    private static readonly Lazy<Currency> cuc = new(() => new Currency("Конвертируемое песо", "Peso Convertible", 931, "CUC", '$'), true);
    private static readonly Lazy<Currency> cup = new(() => new Currency("Кубинское песо", "Cuban Peso", 192, "CUP", "2", '$'), true);
    private static readonly Lazy<Currency> cve = new(() => new Currency("Эскудо Кабо-Верде", "Cape Verde Escudo", 132, "CVE", '$'), true);
    private static readonly Lazy<Currency> czk = new(() => new Currency("Чешская крона", "Czech Koruna", 203, "CZK", "4"), true);
    private static readonly Lazy<Currency> djf = new(() => new Currency("Франк Джибути", "Djibouti Franc", 262, "DJF"), true);
    private static readonly Lazy<Currency> dkk = new(() => new Currency("Датская крона", "Danish Krone", 208, "DKK"), true);
    private static readonly Lazy<Currency> dop = new(() => new Currency("Доминиканское песо", "Dominican Peso", 214, "DOP", '$'), true);
    private static readonly Lazy<Currency> dzd = new(() => new Currency("Алжирский динар", "Algerian Dinar", 012, "DZD", "E71"), true);
    private static readonly Lazy<Currency> egp = new(() => new Currency("Египетский фунт", "Egyptian Pound", 818, "EGP", '£'), true);
    private static readonly Lazy<Currency> ern = new(() => new Currency("Накфа", "Nakfa", 232, "ERN"), true);
    private static readonly Lazy<Currency> etb = new(() => new Currency("Эфиопский быр", "Ethiopian Birr", 230, "ETB", "C27"), true);
    private static readonly Lazy<Currency> eur = new(() => new Currency("Евро", "Euro", 978, "EUR", "31", '€'), true);
    private static readonly Lazy<Currency> fjd = new(() => new Currency("Доллар Фиджи", "Fiji Dollar", 242, "FJD", '$'), true);
    private static readonly Lazy<Currency> fkp = new(() => new Currency("Фунт Фолклендских островов", "Falkland Islands Pound", 238, "FKP", '£'), true);
    private static readonly Lazy<Currency> gbp = new(() => new Currency("Фунт стерлингов", "Pound Sterling", 826, "GBP", "27", '£'), true);
    private static readonly Lazy<Currency> gel = new(() => new Currency("Лари", "Lari", 981, "GEL"), true);
    private static readonly Lazy<Currency> ghs = new(() => new Currency("Ганский седи", "Ghana Cedi", 936, "GHS", '₵'), true);
    private static readonly Lazy<Currency> gip = new(() => new Currency("Гибралтарский фунт", "Gibraltar Pound", 292, "GIP", '£'), true);
    private static readonly Lazy<Currency> gmd = new(() => new Currency("Даласи", "Dalasi", 270, "GMD"), true);
    private static readonly Lazy<Currency> gnf = new(() => new Currency("Гвинейский франк", "Guinea Franc", 324, "GNF"), true);
    private static readonly Lazy<Currency> gtq = new(() => new Currency("Кетсаль", "Quetzal", 320, "GTQ"), true);
    private static readonly Lazy<Currency> gyd = new(() => new Currency("Гайанский доллар", "Guyana Dollar", 328, "GYD", '$'), true);
    private static readonly Lazy<Currency> hkd = new(() => new Currency("Гонконгский доллар", "Hong Kong Dollar", 344, "HKD", "2", '$'), true);
    private static readonly Lazy<Currency> hnl = new(() => new Currency("Лемпира", "Lempira", 340, "HNL"), true);
    private static readonly Lazy<Currency> hrk = new(() => new Currency("Хорватская куна", "Kuna", 191, "HRK"), true);
    private static readonly Lazy<Currency> htg = new(() => new Currency("Гурд", "Gourde", 332, "HTG"), true);
    private static readonly Lazy<Currency> huf = new(() => new Currency("Форинт", "Forint", 348, "HUF", "K85"), true);
    private static readonly Lazy<Currency> idr = new(() => new Currency("Рупия", "Rupiah", 360, "IDR", '₨'), true);
    private static readonly Lazy<Currency> ils = new(() => new Currency("Новый израильский шекель", "New Israeli Sheqel", 376, "ILS", '₪'), true);
    private static readonly Lazy<Currency> inr = new(() => new Currency("Индийская рупия", "Indian Rupee", 356, "INR", "2", '₹'), true);
    private static readonly Lazy<Currency> iqd = new(() => new Currency("Иракский динар", "Iraqi Dinar", 368, "IQD", "3"), true);
    private static readonly Lazy<Currency> irr = new(() => new Currency("Иранский риал", "Iranian Rial", 364, "IRR", '﷼'), true);
    private static readonly Lazy<Currency> isk = new(() => new Currency("Исландская крона", "Iceland Krona", 352, "ISK"), true);
    private static readonly Lazy<Currency> jmd = new(() => new Currency("Ямайский доллар", "Jamaican Dollar", 388, "JMD", '$'), true);
    private static readonly Lazy<Currency> jod = new(() => new Currency("Иорданский динар", "Jordanian Dinar", 400, "JOD"), true);
    private static readonly Lazy<Currency> jpy = new(() => new Currency("Иена", "Yen", 392, "JPY", "2", '¥'), true);
    private static readonly Lazy<Currency> kes = new(() => new Currency("Кенийский шиллинг", "Kenyan Shilling", 404, "KES"), true);
    private static readonly Lazy<Currency> kgs = new(() => new Currency("Сом", "Som", 417, "KGS"), true);
    private static readonly Lazy<Currency> khr = new(() => new Currency("Риель", "Riel", 116, "KHR", "C23", '៛'), true);
    private static readonly Lazy<Currency> kmf = new(() => new Currency("Франк Комор", "Comoro Franc", 174, "KMF"), true);
    private static readonly Lazy<Currency> kpw = new(() => new Currency("Северокорейская вона", "North Korean Won", 408, "KPW", "2", '₩'), true);
    private static readonly Lazy<Currency> krw = new(() => new Currency("Вона", "Won", 410, "KRW", "K09", '₩'), true);
    private static readonly Lazy<Currency> kwd = new(() => new Currency("Кувейтский динар", "Kuwaiti Dinar", 414, "KWD"), true);
    private static readonly Lazy<Currency> kyd = new(() => new Currency("Доллар Островов Кайман", "Cayman Islands Dollar", 136, "KYD", '$'), true);
    private static readonly Lazy<Currency> kzt = new(() => new Currency("Тенге", "Tenge", 398, "KZT", '₸'), true);
    private static readonly Lazy<Currency> lak = new(() => new Currency("Кип", "Kip", 418, "LAK", '₭'), true);
    private static readonly Lazy<Currency> lbp = new(() => new Currency("Ливанский фунт", "Lebanese Pound", 422, "LBP", '£'), true);
    private static readonly Lazy<Currency> lkr = new(() => new Currency("Шри-ланкийская рупия", "Sri Lanka Rupee", 144, "LKR", "A43", '₨'), true);
    private static readonly Lazy<Currency> lrd = new(() => new Currency("Либерийский доллар", "Liberian Dollar", 430, "LRD", '$'), true);
    private static readonly Lazy<Currency> lsl = new(() => new Currency("Лоти", "Loti", 426, "LSL"), true);
    private static readonly Lazy<Currency> ltl = new(() => new Currency("Литовский лит", "Lithuanian Litas", 440, "LTL"), true);
    private static readonly Lazy<Currency> lyd = new(() => new Currency("Ливийский динар", "Libyan Dinar", 434, "LYD"), true);
    private static readonly Lazy<Currency> mad = new(() => new Currency("Марокканский дирхам", "Moroccan Dirham", 504, "MAD"), true);
    private static readonly Lazy<Currency> mdl = new(() => new Currency("Молдавский лей", "Moldovan Leu", 498, "MDL"), true);
    private static readonly Lazy<Currency> mga = new(() => new Currency("Малагасийский ариари", "Malagasy Ariary", 969, "MGA"), true);
    private static readonly Lazy<Currency> mkd = new(() => new Currency("Денар", "Denar", 807, "MKD"), true);
    private static readonly Lazy<Currency> mmk = new(() => new Currency("Кьят", "Kyat", 104, "MMK"), true);
    private static readonly Lazy<Currency> mnt = new(() => new Currency("Тугрик", "Tugrik", 496, "MNT", "4", '₮'), true);
    private static readonly Lazy<Currency> mop = new(() => new Currency("Патака", "Pataca", 446, "MOP"), true);
    private static readonly Lazy<Currency> mru = new(() => new Currency("Угия", "Ouguiya", 929, "MRU"), true);
    private static readonly Lazy<Currency> mur = new(() => new Currency("Маврикийская рупия", "Mauritius Rupee", 480, "MUR", '₨'), true);
    private static readonly Lazy<Currency> mvr = new(() => new Currency("Руфия", "Rufiyaa", 462, "MVR"), true);
    private static readonly Lazy<Currency> mwk = new(() => new Currency("Квача", "Kwacha", 454, "MWK"), true);
    private static readonly Lazy<Currency> mxn = new(() => new Currency("Мексиканское песо", "Mexican Peso", 484, "MXN", "E43", '$'), true);
    private static readonly Lazy<Currency> mxv = new(() => new Currency("Мексиканская инверсионная единица", "Mexican Unidad de Inversion", 979, "MXV"), true);
    private static readonly Lazy<Currency> myr = new(() => new Currency("Малайзийский ринггит", "Malaysian Ringgit", 458, "MYR", "A36"), true);
    private static readonly Lazy<Currency> mzn = new(() => new Currency("Мозамбикский метикал", "Mozambique Metical", 943, "MZN"), true);
    private static readonly Lazy<Currency> nad = new(() => new Currency("Доллар Намибии", "Namibia Dollar", 516, "NAD", '$'), true);
    private static readonly Lazy<Currency> ngn = new(() => new Currency("Найра", "Naira", 566, "NGN", '₦'), true);
    private static readonly Lazy<Currency> nio = new(() => new Currency("Золотая кордоба", "Cordoba Oro", 558, "NIO"), true);
    private static readonly Lazy<Currency> nok = new(() => new Currency("Норвежская крона", "Norwegian Krone", 578, "NOK"), true);
    private static readonly Lazy<Currency> npr = new(() => new Currency("Непальская рупия", "Nepalese Rupee", 524, "NPR", '₨'), true);
    private static readonly Lazy<Currency> nzd = new(() => new Currency("Новозеландский доллар", "New Zealand Dollar", 554, "NZD", '$'), true);
    private static readonly Lazy<Currency> omr = new(() => new Currency("Оманский риал", "Rial Omani", 512, "OMR", '﷼'), true);
    private static readonly Lazy<Currency> pab = new(() => new Currency("Бальбоа", "Balboa", 590, "PAB"), true);
    private static readonly Lazy<Currency> pen = new(() => new Currency("Соль", "Sol", 604, "PEN"), true);
    private static readonly Lazy<Currency> pgk = new(() => new Currency("Кина", "Kina", 598, "PGK"), true);
    private static readonly Lazy<Currency> php = new(() => new Currency("Филиппинское песо", "Philippine Peso", 608, "PHP", '₱'), true);
    private static readonly Lazy<Currency> pkr = new(() => new Currency("Пакистанская рупия", "Pakistan Rupee", 586, "PKR", "2", '₨'), true);
    private static readonly Lazy<Currency> pln = new(() => new Currency("Злотый", "Zloty", 985, "PLN", "3"), true);
    private static readonly Lazy<Currency> pyg = new(() => new Currency("Гуарани", "Guarani", 600, "PYG", '₲'), true);
    private static readonly Lazy<Currency> qar = new(() => new Currency("Катарский риал", "Qatari Rial", 634, "QAR", '﷼'), true);
    private static readonly Lazy<Currency> ron = new(() => new Currency("Новый румынский лей", "Romanian Leu", 946, "RON", "2"), true);
    private static readonly Lazy<Currency> rsd = new(() => new Currency("Сербский динар", "Serbian Dinar", 941, "RSD"), true);
    private static readonly Lazy<Currency> rub = new(() => new Currency("Российский рубль", "Russian Ruble", 643, "RUB", '₽'), true);
    private static readonly Lazy<Currency> rwf = new(() => new Currency("Франк Руанды", "Rwanda Franc", 646, "RWF"), true);
    private static readonly Lazy<Currency> sar = new(() => new Currency("Саудовский риял", "Saudi Riyal", 682, "SAR"), true);
    private static readonly Lazy<Currency> sbd = new(() => new Currency("Доллар Соломоновых Островов", "Solomon Islands Dollar", 090, "SBD", '$'), true);
    private static readonly Lazy<Currency> scr = new(() => new Currency("Сейшельская рупия", "Seychelles Rupee", 690, "SCR", '₨'), true);
    private static readonly Lazy<Currency> sdg = new(() => new Currency("Суданский фунт", "Sudanese Pound", 938, "SDG", "2", '£'), true);
    private static readonly Lazy<Currency> sek = new(() => new Currency("Шведская крона", "Swedish Krona", 752, "SEK", "B07"), true);
    private static readonly Lazy<Currency> sgd = new(() => new Currency("Сингапурский доллар", "Singapore Dollar", 702, "SGD", '$'), true);
    private static readonly Lazy<Currency> shp = new(() => new Currency("Фунт Святой Елены", "Saint Helena Pound", 654, "SHP", '£'), true);
    private static readonly Lazy<Currency> sll = new(() => new Currency("Леоне", "Leone", 694, "SLL"), true);
    private static readonly Lazy<Currency> sos = new(() => new Currency("Сомалийский шиллинг", "Somali Shilling", 706, "SOS"), true);
    private static readonly Lazy<Currency> srd = new(() => new Currency("Суринамский доллар", "Surinam Dollar", 968, "SRD", '$'), true);
    private static readonly Lazy<Currency> ssp = new(() => new Currency("Южносуданский фунт", "South Sudanese Pound", 728, "SSP", '£'), true);
    private static readonly Lazy<Currency> stn = new(() => new Currency("Добра", "Dobra", 930, "STN"), true);
    private static readonly Lazy<Currency> svc = new(() => new Currency("Сальвадорский колон", "El Salvador Colon", 222, "SVC", '₡'), true);
    private static readonly Lazy<Currency> syp = new(() => new Currency("Сирийский фунт", "Syrian Pound", 760, "SYP", '£'), true);
    private static readonly Lazy<Currency> szl = new(() => new Currency("Лилангени", "Lilangeni", 748, "SZL"), true);
    private static readonly Lazy<Currency> thb = new(() => new Currency("Бат", "Baht", 764, "THB", "B70", '฿'), true);
    private static readonly Lazy<Currency> tjs = new(() => new Currency("Сомони", "Somoni", 972, "TJS"), true);
    private static readonly Lazy<Currency> tmt = new(() => new Currency("Новый туркменский манат", "Turkmenistan New Manat", 934, "TMT"), true);
    private static readonly Lazy<Currency> tnd = new(() => new Currency("Тунисский динар", "Tunisian Dinar", 788, "TND", "B76"), true);
    private static readonly Lazy<Currency> top = new(() => new Currency("Паанга", "Pa’anga", 776, "TOP"), true);
    private static readonly Lazy<Currency> @try = new(() => new Currency("Турецкая лира", "Turkish Lira", 949, "TRY", "A13", '₺'), true);
    private static readonly Lazy<Currency> ttd = new(() => new Currency("Доллар Тринидада и Тобаго", "Trinidad and Tobago Dollar", 780, "TTD", '$'), true);
    private static readonly Lazy<Currency> twd = new(() => new Currency("Новый тайваньский доллар", "New Taiwan Dollar", 901, "TWD", '$'), true);
    private static readonly Lazy<Currency> tzs = new(() => new Currency("Танзанийский шиллинг", "Tanzanian Shilling", 834, "TZS"), true);
    private static readonly Lazy<Currency> uah = new(() => new Currency("Гривна", "Hryvnia", 980, "UAH", '₴'), true);
    private static readonly Lazy<Currency> ugx = new(() => new Currency("Угандийский шиллинг", "Uganda Shilling", 800, "UGX"), true);
    private static readonly Lazy<Currency> usd = new(() => new Currency("Доллар США", "US Dollar", 840, "USD", "119", '$'), true);
    private static readonly Lazy<Currency> usn = new(() => new Currency("Доллар следующего дня", "US Dollar (Next day)", 997, "USN", '$'), true);
    private static readonly Lazy<Currency> uss = new(() => new Currency("Доллар того же дня", "US Dollar (Same day)", 998, "USS", '$'), true);
    private static readonly Lazy<Currency> uyi = new(() => new Currency("Уругвайское песо в индексированных единицах", "Urguguay Peso en Unidades Indexadas", 940, "UYI", '$'), true);
    private static readonly Lazy<Currency> uyu = new(() => new Currency("Уругвайское песо", "Peso Uruguayo", 858, "UYU", '$'), true);
    private static readonly Lazy<Currency> uzs = new(() => new Currency("Узбекский сум", "Uzbekistan Sum", 860, "UZS"), true);
    private static readonly Lazy<Currency> vef = new(() => new Currency("Боливар фуэрте", "Bolivar", 937, "VEF"), true);
    private static readonly Lazy<Currency> vnd = new(() => new Currency("Донг", "Dong", 704, "VND", "2", '₫'), true);
    private static readonly Lazy<Currency> vuv = new(() => new Currency("Вату", "Vatu", 548, "VUV"), true);
    private static readonly Lazy<Currency> wst = new(() => new Currency("Тала", "Tala", 882, "WST"), true);
    private static readonly Lazy<Currency> xaf = new(() => new Currency("Франк КФА BEAC", "CFA Franc BEAC", 950, "XAF", "2"), true);
    private static readonly Lazy<Currency> xag = new(() => new Currency("Серебро (тройская унция)", "Silver", 961, "XAG", "A91"), true);
    private static readonly Lazy<Currency> xau = new(() => new Currency("Золото (тройская унция)", "Gold", 959, "XAU", "A90"), true);
    private static readonly Lazy<Currency> xba = new(() => new Currency("Европейская составная единица EURCO", "European Composite Unit (EURCO)", 955, "XBA"), true);
    private static readonly Lazy<Currency> xbb = new(() => new Currency("Европейская валютная единица EMU-6", "European Monetary Unit (E.M.U.-6)", 956, "XBB"), true);
    private static readonly Lazy<Currency> xbc = new(() => new Currency("Европейская расчётная единица EUA-9", "European Unit of Account 9 (E.U.A.-9)", 957, "XBC"), true);
    private static readonly Lazy<Currency> xbd = new(() => new Currency("Европейская расчётная единица EUA-17", "European Unit of Account 17 (E.U.A.-17)", 958, "XBD"), true);
    private static readonly Lazy<Currency> xcd = new(() => new Currency("Восточно-карибский доллар", "East Caribbean Dollar", 951, "XCD", '$'), true);
    private static readonly Lazy<Currency> xdr = new(() => new Currency("СДР (специальные права заимствования)", "SDR (Special Drawing Right)", 960, "XDR"), true);
    private static readonly Lazy<Currency> xof = new(() => new Currency("Франк КФА BCEAO", "CFA Franc BCEAO", 952, "XOF"), true);
    private static readonly Lazy<Currency> xpd = new(() => new Currency("Палладий (тройская унция)", "Palladium", 964, "XPD", "A34"), true);
    private static readonly Lazy<Currency> xpf = new(() => new Currency("Франк КФП", "CFP Franc", 953, "XPF"), true);
    private static readonly Lazy<Currency> xpt = new(() => new Currency("Платина (тройская унция)", "Platinum", 962, "XPT", "A92"), true);
    private static readonly Lazy<Currency> xsu = new(() => new Currency("Сукре", "Sucre", 994, "XSU"), true);
    private static readonly Lazy<Currency> xts = new(() => new Currency("Тестовый код", "Testing Code", 963, "XTS"), true);
    private static readonly Lazy<Currency> xua = new(() => new Currency("Расчётная единица ADB", "ADB Unit of Account", 965, "XUA"), true);
    private static readonly Lazy<Currency> xxx = new(() => new Currency("Без валюты", "No Currency", 999, "XXX"), true);
    private static readonly Lazy<Currency> yer = new(() => new Currency("Йеменский риал", "Yemeni Rial", 886, "YER", "2", '﷼'), true);
    private static readonly Lazy<Currency> zar = new(() => new Currency("Рэнд", "Rand", 710, "ZAR"), true);
    private static readonly Lazy<Currency> zmw = new(() => new Currency("Замбийская квача", "Zambian Kwacha", 967, "ZMW"), true);
    private static readonly Lazy<Currency> zwl = new(() => new Currency("Доллар Зимбабве", "Zimbabwe Dollar", 932, "ZWL", '$'), true);
    private static readonly Lazy<Currency> imp = new(() => new Currency("Фунты Острова Мэн", "Manx pound", 0, "IMP", "IMP", "B91", '£'), true);
    private static readonly Lazy<Currency> ggp = new(() => new Currency("Гернсийский фунт", "Guernsey pound", 0, "GGP", "GGP", '£'), true);
    private static readonly Lazy<Currency> jep = new(() => new Currency("Джерсийский фунт", "Jersey pound", 0, "JEP", "JEP", '£'), true);

    #endregion

    #region Коды

    /// <summary>
    /// Дирхам (ОАЭ).
    /// </summary>
    public static Currency AED => aed.Value;

    /// <summary>
    /// Афгани.
    /// </summary>
    public static Currency AFN => afn.Value;

    /// <summary>
    /// Лек.
    /// </summary>
    public static Currency ALL => all.Value;

    /// <summary>
    /// Армянский драм.
    /// </summary>
    public static Currency AMD => amd.Value;

    /// <summary>
    /// Нидерландский антильский гульден.
    /// </summary>
    public static Currency ANG => ang.Value;

    /// <summary>
    /// Кванза.
    /// </summary>
    public static Currency AOA => aoa.Value;

    /// <summary>
    /// Аргентинское песо.
    /// </summary>
    public static Currency ARS => ars.Value;

    /// <summary>
    /// Австралийский доллар.
    /// </summary>
    public static Currency AUD => aud.Value;

    /// <summary>
    /// Арубанский флорин.
    /// </summary>
    public static Currency AWG => awg.Value;

    /// <summary>
    /// Азербайджанский манат.
    /// </summary>
    public static Currency AZN => azn.Value;

    /// <summary>
    /// Конвертируемая марка.
    /// </summary>
    public static Currency BAM => bam.Value;

    /// <summary>
    /// Барбадосский доллар.
    /// </summary>
    public static Currency BBD => bbd.Value;

    /// <summary>
    /// Така.
    /// </summary>
    public static Currency BDT => bdt.Value;

    /// <summary>
    /// Болгарский лев.
    /// </summary>
    public static Currency BGN => bgn.Value;

    /// <summary>
    /// Бахрейнский динар.
    /// </summary>
    public static Currency BHD => bhd.Value;

    /// <summary>
    /// Бурундийский франк.
    /// </summary>
    public static Currency BIF => bif.Value;

    /// <summary>
    /// Бермудский доллар.
    /// </summary>
    public static Currency BMD => bmd.Value;

    /// <summary>
    /// Брунейский доллар.
    /// </summary>
    public static Currency BND => bnd.Value;

    /// <summary>
    /// Боливиано.
    /// </summary>
    public static Currency BOB => bob.Value;

    /// <summary>
    /// Мвдол.
    /// </summary>
    public static Currency BOV => bov.Value;

    /// <summary>
    /// Бразильский реал.
    /// </summary>
    public static Currency BRL => brl.Value;

    /// <summary>
    /// Багамский доллар.
    /// </summary>
    public static Currency BSD => bsd.Value;

    /// <summary>
    /// Нгултрум.
    /// </summary>
    public static Currency BTN => btn.Value;

    /// <summary>
    /// Пула.
    /// </summary>
    public static Currency BWP => bwp.Value;

    /// <summary>
    /// Белорусский рубль.
    /// </summary>
    public static Currency BYN => byn.Value;

    /// <summary>
    /// Белизский доллар.
    /// </summary>
    public static Currency BZD => bzd.Value;

    /// <summary>
    /// Канадский доллар.
    /// </summary>
    public static Currency CAD => cad.Value;

    /// <summary>
    /// Конголезский франк.
    /// </summary>
    public static Currency CDF => cdf.Value;

    /// <summary>
    /// WIR-евро.
    /// </summary>
    public static Currency CHE => che.Value;

    /// <summary>
    /// Швейцарский франк.
    /// </summary>
    public static Currency CHF => chf.Value;

    /// <summary>
    /// WIR-франк.
    /// </summary>
    public static Currency CHW => chw.Value;

    /// <summary>
    /// Единица развития.
    /// </summary>
    public static Currency CLF => clf.Value;

    /// <summary>
    /// Чилийское песо.
    /// </summary>
    public static Currency CLP => clp.Value;
    /// <summary>
    /// Юань.
    /// </summary>
    public static Currency CNY => cny.Value;
    /// <summary>
    /// Колумбийское песо.
    /// </summary>
    public static Currency COP => cop.Value;
    /// <summary>
    /// Единица реальной стоимости.
    /// </summary>
    public static Currency COU => cou.Value;

    /// <summary>
    /// Коста-риканский колон.
    /// </summary>
    public static Currency CRC => crc.Value;

    /// <summary>
    /// Конвертируемое песо.
    /// </summary>
    public static Currency CUC => cuc.Value;

    /// <summary>
    /// Кубинское песо.
    /// </summary>
    public static Currency CUP => cup.Value;

    /// <summary>
    /// Эскудо Кабо-Верде.
    /// </summary>
    public static Currency CVE => cve.Value;

    /// <summary>
    /// Чешская крона.
    /// </summary>
    public static Currency CZK => czk.Value;

    /// <summary>
    /// Франк Джибути.
    /// </summary>
    public static Currency DJF => djf.Value;

    /// <summary>
    /// Датская крона.
    /// </summary>
    public static Currency DKK => dkk.Value;

    /// <summary>
    /// Доминиканское песо.
    /// </summary>
    public static Currency DOP => dop.Value;

    /// <summary>
    /// Алжирский динар.
    /// </summary>
    public static Currency DZD => dzd.Value;

    /// <summary>
    /// Египетский фунт.
    /// </summary>
    public static Currency EGP => egp.Value;

    /// <summary>
    /// Накфа.
    /// </summary>
    public static Currency ERN => ern.Value;

    /// <summary>
    /// Эфиопский быр.
    /// </summary>
    public static Currency ETB => etb.Value;

    /// <summary>
    /// Евро.
    /// </summary>
    public static Currency EUR => eur.Value;

    /// <summary>
    /// Доллар Фиджи.
    /// </summary>
    public static Currency FJD => fjd.Value;

    /// <summary>
    /// Фунт Фолклендских островов.
    /// </summary>
    public static Currency FKP => fkp.Value;

    /// <summary>
    /// Фунт стерлингов.
    /// </summary>
    public static Currency GBP => gbp.Value;

    /// <summary>
    /// Лари.
    /// </summary>
    public static Currency GEL => gel.Value;

    /// <summary>
    /// Ганский седи.
    /// </summary>
    public static Currency GHS => ghs.Value;

    /// <summary>
    /// Гибралтарский фунт.
    /// </summary>
    public static Currency GIP => gip.Value;

    /// <summary>
    /// Даласи.
    /// </summary>
    public static Currency GMD => gmd.Value;

    /// <summary>
    /// Гвинейский франк.
    /// </summary>
    public static Currency GNF => gnf.Value;

    /// <summary>
    /// Кетсаль.
    /// </summary>
    public static Currency GTQ => gtq.Value;

    /// <summary>
    /// Гайанский доллар.
    /// </summary>
    public static Currency GYD => gyd.Value;

    /// <summary>
    /// Гонконгский доллар.
    /// </summary>
    public static Currency HKD => hkd.Value;

    /// <summary>
    /// Лемпира.
    /// </summary>
    public static Currency HNL => hnl.Value;

    /// <summary>
    /// Хорватская куна.
    /// </summary>
    public static Currency HRK => hrk.Value;

    /// <summary>
    /// Гурд.
    /// </summary>
    public static Currency HTG => htg.Value;

    /// <summary>
    /// Форинт.
    /// </summary>
    public static Currency HUF => huf.Value;

    /// <summary>
    /// Рупия.
    /// </summary>
    public static Currency IDR => idr.Value;

    /// <summary>
    /// Новый израильский шекель.
    /// </summary>
    public static Currency ILS => ils.Value;

    /// <summary>
    /// Индийская рупия.
    /// </summary>
    public static Currency INR => inr.Value;

    /// <summary>
    /// Иракский динар.
    /// </summary>
    public static Currency IQD => iqd.Value;

    /// <summary>
    /// Иранский риал.
    /// </summary>
    public static Currency IRR => irr.Value;

    /// <summary>
    /// Исландская крона.
    /// </summary>
    public static Currency ISK => isk.Value;

    /// <summary>
    /// Ямайский доллар.
    /// </summary>
    public static Currency JMD => jmd.Value;

    /// <summary>
    /// Иорданский динар.
    /// </summary>
    public static Currency JOD => jod.Value;

    /// <summary>
    /// Иена.
    /// </summary>
    public static Currency JPY => jpy.Value;

    /// <summary>
    /// Кенийский шиллинг.
    /// </summary>
    public static Currency KES => kes.Value;

    /// <summary>
    /// Сом.
    /// </summary>
    public static Currency KGS => kgs.Value;

    /// <summary>
    /// Риель.
    /// </summary>
    public static Currency KHR => khr.Value;

    /// <summary>
    /// Франк Комор.
    /// </summary>
    public static Currency KMF => kmf.Value;

    /// <summary>
    /// Северокорейская вона.
    /// </summary>
    public static Currency KPW => kpw.Value;

    /// <summary>
    /// Вона.
    /// </summary>
    public static Currency KRW => krw.Value;

    /// <summary>
    /// Кувейтский динар.
    /// </summary>
    public static Currency KWD => kwd.Value;

    /// <summary>
    /// Доллар Островов Кайман.
    /// </summary>
    public static Currency KYD => kyd.Value;

    /// <summary>
    /// Тенге.
    /// </summary>
    public static Currency KZT => kzt.Value;

    /// <summary>
    /// Кип.
    /// </summary>
    public static Currency LAK => lak.Value;

    /// <summary>
    /// Ливанский фунт.
    /// </summary>
    public static Currency LBP => lbp.Value;

    /// <summary>
    /// Шри-ланкийская рупия.
    /// </summary>
    public static Currency LKR => lkr.Value;

    /// <summary>
    /// Либерийский доллар.
    /// </summary>
    public static Currency LRD => lrd.Value;

    /// <summary>
    /// Лоти.
    /// </summary>
    public static Currency LSL => lsl.Value;

    /// <summary>
    /// Литовский лит.
    /// </summary>
    public static Currency LTL => ltl.Value;

    /// <summary>
    /// Ливийский динар.
    /// </summary>
    public static Currency LYD => lyd.Value;

    /// <summary>
    /// Марокканский дирхам.
    /// </summary>
    public static Currency MAD => mad.Value;

    /// <summary>
    /// Молдавский лей.
    /// </summary>
    public static Currency MDL => mdl.Value;

    /// <summary>
    /// Малагасийский ариари.
    /// </summary>
    public static Currency MGA => mga.Value;

    /// <summary>
    /// Денар.
    /// </summary>
    public static Currency MKD => mkd.Value;

    /// <summary>
    /// Кьят.
    /// </summary>
    public static Currency MMK => mmk.Value;

    /// <summary>
    /// Тугрик.
    /// </summary>
    public static Currency MNT => mnt.Value;

    /// <summary>
    /// Патака.
    /// </summary>
    public static Currency MOP => mop.Value;

    /// <summary>
    /// Угия.
    /// </summary>
    public static Currency MRU => mru.Value;

    /// <summary>
    /// Маврикийская рупия.
    /// </summary>
    public static Currency MUR => mur.Value;

    /// <summary>
    /// Руфия.
    /// </summary>
    public static Currency MVR => mvr.Value;

    /// <summary>
    /// Квача.
    /// </summary>
    public static Currency MWK => mwk.Value;

    /// <summary>
    /// Мексиканское песо.
    /// </summary>
    public static Currency MXN => mxn.Value;
    /// <summary>
    /// Мексиканская инверсионная единица.
    /// </summary>
    public static Currency MXV => mxv.Value;

    /// <summary>
    /// Малайзийский ринггит.
    /// </summary>
    public static Currency MYR => myr.Value;

    /// <summary>
    /// Мозамбикский метикал.
    /// </summary>
    public static Currency MZN => mzn.Value;

    /// <summary>
    /// Доллар Намибии.
    /// </summary>
    public static Currency NAD => nad.Value;

    /// <summary>
    /// Найра.
    /// </summary>
    public static Currency NGN => ngn.Value;

    /// <summary>
    /// Золотая кордоба.
    /// </summary>
    public static Currency NIO => nio.Value;

    /// <summary>
    /// Норвежская крона.
    /// </summary>
    public static Currency NOK => nok.Value;

    /// <summary>
    /// Непальская рупия.
    /// </summary>
    public static Currency NPR => npr.Value;

    /// <summary>
    /// Новозеландский доллар.
    /// </summary>
    public static Currency NZD => nzd.Value;

    /// <summary>
    /// Оманский риал.
    /// </summary>
    public static Currency OMR => omr.Value;

    /// <summary>
    /// Бальбоа.
    /// </summary>
    public static Currency PAB => pab.Value;

    /// <summary>
    /// Соль.
    /// </summary>
    public static Currency PEN => pen.Value;

    /// <summary>
    /// Кина.
    /// </summary>
    public static Currency PGK => pgk.Value;

    /// <summary>
    /// Филиппинское песо.
    /// </summary>
    public static Currency PHP => php.Value;

    /// <summary>
    /// Пакистанская рупия.
    /// </summary>
    public static Currency PKR => pkr.Value;

    /// <summary>
    /// Злотый.
    /// </summary>
    public static Currency PLN => pln.Value;

    /// <summary>
    /// Гуарани.
    /// </summary>
    public static Currency PYG => pyg.Value;

    /// <summary>
    /// Катарский риал.
    /// </summary>
    public static Currency QAR => qar.Value;

    /// <summary>
    /// Новый румынский лей.
    /// </summary>
    public static Currency RON => ron.Value;

    /// <summary>
    /// Сербский динар.
    /// </summary>
    public static Currency RSD => rsd.Value;

    /// <summary>
    /// Российский рубль.
    /// </summary>
    public static Currency RUB => rub.Value;

    /// <summary>
    /// Франк Руанды.
    /// </summary>
    public static Currency RWF => rwf.Value;

    /// <summary>
    /// Саудовский риял.
    /// </summary>
    public static Currency SAR => sar.Value;

    /// <summary>
    /// Доллар Соломоновых Островов.
    /// </summary>
    public static Currency SBD => sbd.Value;

    /// <summary>
    /// Сейшельская рупия.
    /// </summary>
    public static Currency SCR => scr.Value;

    /// <summary>
    /// Суданский фунт.
    /// </summary>
    public static Currency SDG => sdg.Value;

    /// <summary>
    /// Шведская крона.
    /// </summary>
    public static Currency SEK => sek.Value;

    /// <summary>
    /// Сингапурский доллар.
    /// </summary>
    public static Currency SGD => sgd.Value;

    /// <summary>
    /// Фунт Святой Елены.
    /// </summary>
    public static Currency SHP => shp.Value;

    /// <summary>
    /// Леоне.
    /// </summary>
    public static Currency SLL => sll.Value;

    /// <summary>
    /// Сомалийский шиллинг.
    /// </summary>
    public static Currency SOS => sos.Value;

    /// <summary>
    /// Суринамский доллар.
    /// </summary>
    public static Currency SRD => srd.Value;

    /// <summary>
    /// Южносуданский фунт.
    /// </summary>
    public static Currency SSP => ssp.Value;

    /// <summary>
    /// Добра.
    /// </summary>
    public static Currency STN => stn.Value;

    /// <summary>
    /// Сальвадорский колон.
    /// </summary>
    public static Currency SVC => svc.Value;

    /// <summary>
    /// Сирийский фунт.
    /// </summary>
    public static Currency SYP => syp.Value;

    /// <summary>
    /// Лилангени.
    /// </summary>
    public static Currency SZL => szl.Value;

    /// <summary>
    /// Бат.
    /// </summary>
    public static Currency THB => thb.Value;

    /// <summary>
    /// Сомони.
    /// </summary>
    public static Currency TJS => tjs.Value;

    /// <summary>
    /// Новый туркменский манат.
    /// </summary>
    public static Currency TMT => tmt.Value;

    /// <summary>
    /// Тунисский динар.
    /// </summary>
    public static Currency TND => tnd.Value;

    /// <summary>
    /// Паанга.
    /// </summary>
    public static Currency TOP => top.Value;

    /// <summary>
    /// Турецкая лира.
    /// </summary>
    public static Currency TRY => @try.Value;

    /// <summary>
    /// Доллар Тринидада и Тобаго.
    /// </summary>
    public static Currency TTD => ttd.Value;

    /// <summary>
    /// Новый тайваньский доллар.
    /// </summary>
    public static Currency TWD => twd.Value;

    /// <summary>
    /// Танзанийский шиллинг.
    /// </summary>
    public static Currency TZS => tzs.Value;

    /// <summary>
    /// Гривна.
    /// </summary>
    public static Currency UAH => uah.Value;

    /// <summary>
    /// Угандийский шиллинг.
    /// </summary>
    public static Currency UGX => ugx.Value;

    /// <summary>
    /// Доллар США.
    /// </summary>
    public static Currency USD => usd.Value;

    /// <summary>
    /// Доллар следующего дня.
    /// </summary>
    public static Currency USN => usn.Value;
    /// <summary>
    /// Доллар того же дня.
    /// </summary>
    public static Currency USS => uss.Value;

    /// <summary>
    /// Уругвайское песо в индексированных единицах.
    /// </summary>
    public static Currency UYI => uyi.Value;

    /// <summary>
    /// Уругвайское песо.
    /// </summary>
    public static Currency UYU => uyu.Value;

    /// <summary>
    /// Узбекский сум.
    /// </summary>
    public static Currency UZS => uzs.Value;

    /// <summary>
    /// Боливар фуэрте.
    /// </summary>
    public static Currency VEF => vef.Value;

    /// <summary>
    /// Донг.
    /// </summary>
    public static Currency VND => vnd.Value;

    /// <summary>
    /// Вату.
    /// </summary>
    public static Currency VUV => vuv.Value;

    /// <summary>
    /// Тала.
    /// </summary>
    public static Currency WST => wst.Value;

    /// <summary>
    /// Франк КФА BEAC.
    /// </summary>
    public static Currency XAF => xaf.Value;

    /// <summary>
    /// Серебро (тройская унция).
    /// </summary>
    public static Currency XAG => xag.Value;

    /// <summary>
    /// Золото (тройская унция).
    /// </summary>
    public static Currency XAU => xau.Value;

    /// <summary>
    /// Европейская составная единица EURCO.
    /// </summary>
    public static Currency XBA => xba.Value;

    /// <summary>
    /// Европейская валютная единица EMU-6.
    /// </summary>
    public static Currency XBB => xbb.Value;

    /// <summary>
    /// Европейская расчётная единица EUA-9.
    /// </summary>
    public static Currency XBC => xbc.Value;

    /// <summary>
    /// Европейская расчётная единица EUA-17.
    /// </summary>
    public static Currency XBD => xbd.Value;

    /// <summary>
    /// Восточно-карибский доллар.
    /// </summary>
    public static Currency XCD => xcd.Value;

    /// <summary>
    /// СДР (специальные права заимствования).
    /// </summary>
    public static Currency XDR => xdr.Value;

    /// <summary>
    /// Франк КФА BCEAO.
    /// </summary>
    public static Currency XOF => xof.Value;

    /// <summary>
    /// Палладий (тройская унция).
    /// </summary>
    public static Currency XPD => xpd.Value;

    /// <summary>
    /// Франк КФП.
    /// </summary>
    public static Currency XPF => xpf.Value;

    /// <summary>
    /// Платина (тройская унция).
    /// </summary>
    public static Currency XPT => xpt.Value;

    /// <summary>
    /// Сукре.
    /// </summary>
    public static Currency XSU => xsu.Value;

    /// <summary>
    /// Тестовый код.
    /// </summary>
    public static Currency XTS => xts.Value;

    /// <summary>
    /// Расчётная единица ADB.
    /// </summary>
    public static Currency XUA => xua.Value;

    /// <summary>
    /// Без валюты.
    /// </summary>
    public static Currency XXX => xxx.Value;

    /// <summary>
    /// Йеменский риал.
    /// </summary>
    public static Currency YER => yer.Value;

    /// <summary>
    /// Рэнд.
    /// </summary>
    public static Currency ZAR => zar.Value;

    /// <summary>
    /// Замбийская квача.
    /// </summary>
    public static Currency ZMW => zmw.Value;

    /// <summary>
    /// Доллар Зимбабве.
    /// </summary>
    public static Currency ZWL => zwl.Value;

    /// <summary>
    /// Фунты Острова Мэн.
    /// </summary>
    public static Currency IMP => imp.Value;

    /// <summary>
    /// Гернсийский фунт.
    /// </summary>
    public static Currency GGP => ggp.Value;

    /// <summary>
    /// Джерсийский фунт.
    /// </summary>
    public static Currency JEP => jep.Value;

    #endregion

    /// <summary>
    /// Коллекция всех валют.
    /// </summary>
    public static IEnumerable<Currency> All =>
    [
        AED, AFN, ALL, AMD, ANG, AOA, ARS, AUD, AWG, AZN, BAM, BBD, BDT, BGN, BHD, BIF, BMD, BND, BOB, BOV, BRL, BSD, BTN, BWP, BYN, BZD, CAD, CDF, CHE, CHF, CHW, CLF, CLP, CNY,
        COP, COU, CRC, CUC, CUP, CVE, CZK, DJF, DKK, DOP, DZD, EGP, ERN, ETB, EUR, FJD, FKP, GBP, GEL, GHS, GIP, GMD, GNF, GTQ, GYD, HKD, HNL, HRK, HTG, HUF, IDR, ILS, INR, IQD,
        IRR, ISK, JMD, JOD, JPY, KES, KGS, KHR, KMF, KPW, KRW, KWD, KYD, KZT, LAK, LBP, LKR, LRD, LSL, LTL, LYD, MAD, MDL, MGA, MKD, MMK, MNT, MOP, MRU, MUR, MVR, MWK, MXN, MXV,
        MYR, MZN, NAD, NGN, NIO, NOK, NPR, NZD, OMR, PAB, PEN, PGK, PHP, PKR, PLN, PYG, QAR, RON, RSD, RUB, RWF, SAR, SBD, SCR, SDG, SEK, SGD, SHP, SLL, SOS, SRD, SSP, STN, SVC,
        SYP, SZL, THB, TJS, TMT, TND, TOP, TRY, TTD, TWD, TZS, UAH, UGX, USD, USN, USS, UYI, UYU, UZS, VEF, VND, VUV, WST, XAF, XAG, XAU, XBA, XBB, XBC, XBD, XCD, XDR, XOF, XPD,
        XPF, XPT, XSU, XTS, XUA, XXX, YER, ZAR, ZMW, ZWL, IMP, GGP, JEP,
    ];

    #region Конструкторы

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Currency"/>.
    /// </summary>
    /// <param name="name">Название (на русском).</param>
    /// <param name="internationalName">Название (интернациональное).</param>
    /// <param name="code">Цифровой код валюты.</param>
    /// <param name="isoCode">Символьный код валюты (ISO 4217).</param>
    /// <param name="taylorCode">Код Тэйлора.</param>
    /// <param name="kkb">Код KKB.</param>
    public Currency(string name, string internationalName, ushort code, string isoCode, string? taylorCode, string? kkb) : this(name, internationalName, code, isoCode, taylorCode, kkb, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Currency"/>.
    /// </summary>
    /// <param name="name">Название (на русском).</param>
    /// <param name="internationalName">Название (интернациональное).</param> 
    /// <param name="code">Цифровой код валюты.</param>
    /// <param name="isoCode">Символьный код валюты (ISO 4217).</param>
    /// <param name="taylorCode">Код Тэйлора.</param>
    /// <param name="symbol">Символьный код (Unicode).</param>
    public Currency(string name, string internationalName, ushort code, string isoCode, string? taylorCode, char? symbol) : this(name, internationalName, code, isoCode, taylorCode, default, symbol) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Currency"/>.
    /// </summary>
    /// <param name="name">Название (на русском).</param>
    /// <param name="internationalName">Название (интернациональное).</param>
    /// <param name="code">Цифровой код валюты.</param>
    /// <param name="isoCode">Символьный код валюты (ISO 4217).</param>
    /// <param name="symbol">Символьный код (Unicode).</param>
    public Currency(string name, string internationalName, ushort code, string isoCode, char? symbol) : this(name, internationalName, code, isoCode, default, symbol) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Currency"/>.
    /// </summary>
    /// <param name="name">Название (на русском).</param>
    /// <param name="internationalName">Название (интернациональное).</param> 
    /// <param name="code">Цифровой код валюты.</param>
    /// <param name="isoCode">Символьный код валюты (ISO 4217).</param>
    /// <param name="taylorCode">Код Тэйлора.</param>
    public Currency(string name, string internationalName, ushort code, string isoCode, string? taylorCode) : this(name, internationalName, code, isoCode, taylorCode, default, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Currency"/>.
    /// </summary>
    /// <param name="name">Название (на русском).</param>
    /// <param name="internationalName">Название (интернациональное).</param>
    /// <param name="code">Цифровой код валюты.</param>
    /// <param name="isoCode">Символьный код валюты (ISO 4217).</param>
    public Currency(string name, string internationalName, ushort code, string isoCode) : this(name, internationalName, code, isoCode, default, default, default) { }

    #endregion

    /// <summary>
    /// Возвращает хеш-код для объекта.
    /// </summary>
    /// <returns>Хеш-код для объекта.</returns>
    public override int GetHashCode() => IsoCode.GetHashCode(StringComparison.InvariantCultureIgnoreCase);

    /// <summary>
    /// Сравнивает текущий экземпляр <see cref="Currency"/> с заданным объектом.
    /// </summary>
    /// <param name="obj">Объект для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов совпадают, иначе <c>false</c>.
    /// </returns>
    public override bool Equals(object? obj)
    {
        if (obj is string str) return str.GetHashCode(StringComparison.InvariantCultureIgnoreCase) == IsoCode.GetHashCode(StringComparison.InvariantCultureIgnoreCase);
        else if (obj is ushort с) return с.GetHashCode() == Code.GetHashCode();
        else if (obj is Currency currency) return currency.GetHashCode() == GetHashCode();
        else return false;
    }

    /// <summary>
    /// Сравнивает текущий экземпляр <see cref="Currency"/> с заданным экземпляром <see cref="Currency"/>.
    /// </summary>
    /// <param name="other">Экземпляр <see cref="Currency"/> для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов совпадают, иначе <c>false</c>.
    /// </returns>
    public bool Equals(Currency? other) => Equals(other as object);

    /// <summary>
    /// Преобразует текущий экземпляр <see cref="Currency"/> в символьный код валюты.
    /// </summary>
    /// <returns>Символьный код валюты.</returns>
    public override string ToString() => IsoCode;

    /// <summary>
    /// Преобразует текущий экземпляр <see cref="Currency"/> в цифровой код валюты.
    /// </summary>
    /// <returns>Цифровой код валюты.</returns>
    public ushort ToUInt16() => Code;

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его символьному коду.
    /// </summary>
    /// <param name="s">Символьный код валюты.</param>
    /// <param name="provider">Параметры форматирования.</param>
    /// <param name="result">Экземпляр <see cref="Currency"/>.</param>
    /// <returns><c>True</c>, если экземпляр был найден, иначе <c>false</c>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Currency? result)
    {
        result = s?.Trim().ToUpperInvariant() switch
        {
            "AED" => AED,
            "AFN" => AFN,
            "ALL" => ALL,
            "AMD" => AMD,
            "ANG" => ANG,
            "AOA" => AOA,
            "ARS" => ARS,
            "AUD" => AUD,
            "AWG" => AWG,
            "AZN" => AZN,
            "BAM" => BAM,
            "BBD" => BBD,
            "BDT" => BDT,
            "BGN" => BGN,
            "BHD" => BHD,
            "BIF" => BIF,
            "BMD" => BMD,
            "BND" => BND,
            "BOB" => BOB,
            "BOV" => BOV,
            "BRL" => BRL,
            "BSD" => BSD,
            "BTN" => BTN,
            "BWP" => BWP,
            "BYN" => BYN,
            "BZD" => BZD,
            "CAD" => CAD,
            "CDF" => CDF,
            "CHE" => CHE,
            "CHF" => CHF,
            "CHW" => CHW,
            "CLF" => CLF,
            "CLP" => CLP,
            "CNY" => CNY,
            "COP" => COP,
            "COU" => COU,
            "CRC" => CRC,
            "CUC" => CUC,
            "CUP" => CUP,
            "CVE" => CVE,
            "CZK" => CZK,
            "DJF" => DJF,
            "DKK" => DKK,
            "DOP" => DOP,
            "DZD" => DZD,
            "EGP" => EGP,
            "ERN" => ERN,
            "ETB" => ETB,
            "EUR" => EUR,
            "FJD" => FJD,
            "FKP" => FKP,
            "GBP" => GBP,
            "GEL" => GEL,
            "GHS" => GHS,
            "GIP" => GIP,
            "GMD" => GMD,
            "GNF" => GNF,
            "GTQ" => GTQ,
            "GYD" => GYD,
            "HKD" => HKD,
            "HNL" => HNL,
            "HRK" => HRK,
            "HTG" => HTG,
            "HUF" => HUF,
            "IDR" => IDR,
            "ILS" => ILS,
            "INR" => INR,
            "IQD" => IQD,
            "IRR" => IRR,
            "ISK" => ISK,
            "JMD" => JMD,
            "JOD" => JOD,
            "JPY" => JPY,
            "KES" => KES,
            "KGS" => KGS,
            "KHR" => KHR,
            "KMF" => KMF,
            "KPW" => KPW,
            "KRW" => KRW,
            "KWD" => KWD,
            "KYD" => KYD,
            "KZT" => KZT,
            "LAK" => LAK,
            "LBP" => LBP,
            "LKR" => LKR,
            "LRD" => LRD,
            "LSL" => LSL,
            "LTL" => LTL,
            "LYD" => LYD,
            "MAD" => MAD,
            "MDL" => MDL,
            "MGA" => MGA,
            "MKD" => MKD,
            "MMK" => MMK,
            "MNT" => MNT,
            "MOP" => MOP,
            "MRU" => MRU,
            "MUR" => MUR,
            "MVR" => MVR,
            "MWK" => MWK,
            "MXN" => MXN,
            "MXV" => MXV,
            "MYR" => MYR,
            "MZN" => MZN,
            "NAD" => NAD,
            "NGN" => NGN,
            "NIO" => NIO,
            "NOK" => NOK,
            "NPR" => NPR,
            "NZD" => NZD,
            "OMR" => OMR,
            "PAB" => PAB,
            "PEN" => PEN,
            "PGK" => PGK,
            "PHP" => PHP,
            "PKR" => PKR,
            "PLN" => PLN,
            "PYG" => PYG,
            "QAR" => QAR,
            "RON" => RON,
            "RSD" => RSD,
            "RUB" => RUB,
            "RWF" => RWF,
            "SAR" => SAR,
            "SBD" => SBD,
            "SCR" => SCR,
            "SDG" => SDG,
            "SEK" => SEK,
            "SGD" => SGD,
            "SHP" => SHP,
            "SLL" => SLL,
            "SOS" => SOS,
            "SRD" => SRD,
            "SSP" => SSP,
            "STN" => STN,
            "SVC" => SVC,
            "SYP" => SYP,
            "SZL" => SZL,
            "THB" => THB,
            "TJS" => TJS,
            "TMT" => TMT,
            "TND" => TND,
            "TOP" => TOP,
            "TRY" => TRY,
            "TTD" => TTD,
            "TWD" => TWD,
            "TZS" => TZS,
            "UAH" => UAH,
            "UGX" => UGX,
            "USD" => USD,
            "USN" => USN,
            "USS" => USS,
            "UYI" => UYI,
            "UYU" => UYU,
            "UZS" => UZS,
            "VEF" => VEF,
            "VND" => VND,
            "VUV" => VUV,
            "WST" => WST,
            "XAF" => XAF,
            "XAG" => XAG,
            "XAU" => XAU,
            "XBA" => XBA,
            "XBB" => XBB,
            "XBC" => XBC,
            "XBD" => XBD,
            "XCD" => XCD,
            "XDR" => XDR,
            "XOF" => XOF,
            "XPD" => XPD,
            "XPF" => XPF,
            "XPT" => XPT,
            "XSU" => XSU,
            "XTS" => XTS,
            "XUA" => XUA,
            "XXX" => XXX,
            "YER" => YER,
            "ZAR" => ZAR,
            "ZMW" => ZMW,
            "ZWL" => ZWL,
            "IMP" => IMP,
            "GGP" => GGP,
            "JEP" => JEP,

            _ => default,
        };

        return result is not null;
    }

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его символьному коду.
    /// </summary>
    /// <param name="s">Символьный код валюты.</param>
    /// <param name="result">Экземпляр <see cref="Currency"/>.</param>
    /// <returns><c>True</c>, если экземпляр был найден, иначе <c>false</c>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out Currency? result) => TryParse(s, default, out result);

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его цифровому коду.
    /// </summary>
    /// <param name="code">Цифровой код валюты.</param>
    /// <param name="currency">Экземпляр <see cref="Currency"/>.</param>
    /// <returns><c>True</c>, если экземпляр был найден, иначе <c>false</c>.</returns>
    public static bool TryParse(ushort code, [MaybeNullWhen(false)] out Currency? currency)
    {
        currency = code switch
        {
            784 => AED,
            971 => AFN,
            008 => ALL,
            051 => AMD,
            532 => ANG,
            973 => AOA,
            032 => ARS,
            036 => AUD,
            533 => AWG,
            944 => AZN,
            977 => BAM,
            052 => BBD,
            050 => BDT,
            975 => BGN,
            048 => BHD,
            108 => BIF,
            060 => BMD,
            096 => BND,
            068 => BOB,
            984 => BOV,
            986 => BRL,
            044 => BSD,
            064 => BTN,
            072 => BWP,
            933 => BYN,
            084 => BZD,
            124 => CAD,
            976 => CDF,
            947 => CHE,
            756 => CHF,
            948 => CHW,
            990 => CLF,
            152 => CLP,
            156 => CNY,
            170 => COP,
            970 => COU,
            188 => CRC,
            931 => CUC,
            192 => CUP,
            132 => CVE,
            203 => CZK,
            262 => DJF,
            208 => DKK,
            214 => DOP,
            012 => DZD,
            818 => EGP,
            232 => ERN,
            230 => ETB,
            978 => EUR,
            242 => FJD,
            238 => FKP,
            826 => GBP,
            981 => GEL,
            936 => GHS,
            292 => GIP,
            270 => GMD,
            324 => GNF,
            320 => GTQ,
            328 => GYD,
            344 => HKD,
            340 => HNL,
            191 => HRK,
            332 => HTG,
            348 => HUF,
            360 => IDR,
            376 => ILS,
            356 => INR,
            368 => IQD,
            364 => IRR,
            352 => ISK,
            388 => JMD,
            400 => JOD,
            392 => JPY,
            404 => KES,
            417 => KGS,
            116 => KHR,
            174 => KMF,
            408 => KPW,
            410 => KRW,
            414 => KWD,
            136 => KYD,
            398 => KZT,
            418 => LAK,
            422 => LBP,
            144 => LKR,
            430 => LRD,
            426 => LSL,
            440 => LTL,
            434 => LYD,
            504 => MAD,
            498 => MDL,
            969 => MGA,
            807 => MKD,
            104 => MMK,
            496 => MNT,
            446 => MOP,
            929 => MRU,
            480 => MUR,
            462 => MVR,
            454 => MWK,
            484 => MXN,
            979 => MXV,
            458 => MYR,
            943 => MZN,
            516 => NAD,
            566 => NGN,
            558 => NIO,
            578 => NOK,
            524 => NPR,
            554 => NZD,
            512 => OMR,
            590 => PAB,
            604 => PEN,
            598 => PGK,
            608 => PHP,
            586 => PKR,
            985 => PLN,
            600 => PYG,
            634 => QAR,
            946 => RON,
            941 => RSD,
            643 => RUB,
            646 => RWF,
            682 => SAR,
            090 => SBD,
            690 => SCR,
            938 => SDG,
            752 => SEK,
            702 => SGD,
            654 => SHP,
            694 => SLL,
            706 => SOS,
            968 => SRD,
            728 => SSP,
            930 => STN,
            222 => SVC,
            760 => SYP,
            748 => SZL,
            764 => THB,
            972 => TJS,
            934 => TMT,
            788 => TND,
            776 => TOP,
            949 => TRY,
            780 => TTD,
            901 => TWD,
            834 => TZS,
            980 => UAH,
            800 => UGX,
            840 => USD,
            997 => USN,
            998 => USS,
            940 => UYI,
            858 => UYU,
            860 => UZS,
            937 => VEF,
            704 => VND,
            548 => VUV,
            882 => WST,
            950 => XAF,
            961 => XAG,
            959 => XAU,
            955 => XBA,
            956 => XBB,
            957 => XBC,
            958 => XBD,
            951 => XCD,
            960 => XDR,
            952 => XOF,
            964 => XPD,
            953 => XPF,
            962 => XPT,
            994 => XSU,
            963 => XTS,
            965 => XUA,
            999 => XXX,
            886 => YER,
            710 => ZAR,
            967 => ZMW,
            932 => ZWL,

            _ => default,
        };

        return currency is not null;
    }

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его символьному коду. 
    /// </summary>
    /// <param name="s">Символьный код валюты.</param>
    /// <param name="provider">Параметры форматирования.</param>
    /// <returns>Экземпляр <see cref="Currency"/>.</returns>
    /// <exception cref="FormatException"/>
    public static Currency Parse(string s, IFormatProvider? provider) => !TryParse(s, provider, out var result) || result is null ? throw new FormatException() : result;

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его символьному коду. 
    /// </summary>
    /// <param name="s">Символьный код валюты.</param>
    /// <returns>Экземпляр <see cref="Currency"/>.</returns>
    /// <exception cref="FormatException"/>
    public static Currency Parse(string s) => Parse(s, default);

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его цифровому коду. 
    /// </summary>
    /// <param name="code">Цифровой код валюты.</param>
    /// <returns>Экземпляр <see cref="Currency"/>.</returns>
    /// <exception cref="FormatException"/>
    public static Currency Parse(ushort code) => !TryParse(code, out var result) || result is null ? throw new FormatException() : result;

    /// <summary>
    /// Приводит цифровой код валюты к типу <see cref="Currency"/>.
    /// </summary>
    /// <param name="code">Цифровой код валюты.</param>
    /// <returns>Экземпляр <see cref="Currency"/>.</returns>
    /// <exception cref="FormatException"/>
    public static Currency FromUInt16(ushort code) => Parse(code);

    /// <summary>
    /// Приводит символьный код валюты к типу <see cref="Currency"/>.
    /// </summary>
    /// <param name="isoCode">Символьный код валюты.</param>
    /// <returns>Экземпляр <see cref="Currency"/>.</returns>
    /// <exception cref="FormatException"/>
    public static Currency FromString(string isoCode) => Parse(isoCode);

    /// <summary>
    /// Приводит цифровой код валюты к типу <see cref="Currency"/>.
    /// </summary>
    /// <param name="code">Цифровой код валюты.</param>
    public static explicit operator Currency(ushort code) => FromUInt16(code);

    /// <summary>
    /// Приводит символьный код валюты к типу <see cref="Currency"/>.
    /// </summary>
    /// <param name="isoCode">Символьный код валюты.</param>
    public static explicit operator Currency(string isoCode) => FromString(isoCode);

    /// <summary>
    /// Неявно преобразует экземпляр <see cref="Currency"/> в цифровой код валюты.
    /// </summary>
    /// <param name="currency">Экземпляр <see cref="Currency"/>.</param>
    public static implicit operator ushort(Currency? currency) => currency is not null ? currency.ToUInt16() : default;

    /// <summary>
    /// Неявно преобразует экземпляр <see cref="Currency"/> в символьный код валюты.
    /// </summary>
    /// <param name="currency">Экземпляр <see cref="Currency"/>.</param>
    public static implicit operator string(Currency? currency) => currency is not null ? currency.ToString() : string.Empty;

    /// <summary>
    /// Сравнивает экземпляр <see cref="Currency"/> с заданной строкой.
    /// </summary>
    /// <param name="currency">Экземпляр <see cref="Currency"/>.</param>
    /// <param name="str">Строка для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов совпадают, иначе <c>false</c>.
    /// </returns>
    public static bool operator ==(Currency? currency, string? str) => (currency is null && str is null) || (currency is not null && str is not null && currency.Equals(str));

    /// <summary>
    /// Сравнивает экземпляр <see cref="Currency"/> с заданной строкой.
    /// </summary>
    /// <param name="currency">Экземпляр <see cref="Currency"/>.</param>
    /// <param name="str">Строка для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов не совпадают, иначе <c>false</c>.
    /// </returns>
    public static bool operator !=(Currency? currency, string? str) => !(currency == str);
}