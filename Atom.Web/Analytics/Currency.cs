using System.Text.Json.Serialization;
using Atom.Reactive;

namespace Atom.Web.Analytics;

/// <summary>
/// Данные о валюте.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Currency"/>.
/// </remarks>
/// <param name="code">Цифровой код валюты.</param>
/// <param name="isoCode">Символьный код валюты.</param>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
[JsonConverter(typeof(CurrencyJsonConverter))]
public class Currency(ushort code, string isoCode) : Reactively, IParsable<Currency?>
{
    private static readonly Lazy<Currency> Aud = new(() => new Currency(36, "AUD"), true);
    private static readonly Lazy<Currency> Eur = new(() => new Currency(978, "EUR"), true);
    private static readonly Lazy<Currency> Azn = new(() => new Currency(944, "AZN"), true);
    private static readonly Lazy<Currency> All = new(() => new Currency(8, "ALL"), true);
    private static readonly Lazy<Currency> Dzd = new(() => new Currency(12, "DZD"), true);
    private static readonly Lazy<Currency> Xcd = new(() => new Currency(951, "XCD"), true);
    private static readonly Lazy<Currency> Aoa = new(() => new Currency(973, "AOA"), true);
    private static readonly Lazy<Currency> Ars = new(() => new Currency(32, "ARS"), true);
    private static readonly Lazy<Currency> Amd = new(() => new Currency(51, "AMD"), true);
    private static readonly Lazy<Currency> Awg = new(() => new Currency(533, "AWG"), true);
    private static readonly Lazy<Currency> Afn = new(() => new Currency(971, "AFN"), true);
    private static readonly Lazy<Currency> Bsd = new(() => new Currency(44, "BSD"), true);
    private static readonly Lazy<Currency> Bdt = new(() => new Currency(50, "BDT"), true);
    private static readonly Lazy<Currency> Bbd = new(() => new Currency(52, "BBD"), true);
    private static readonly Lazy<Currency> Bhd = new(() => new Currency(48, "BHD"), true);
    private static readonly Lazy<Currency> Byr = new(() => new Currency(974, "BYR"), true);
    private static readonly Lazy<Currency> Bzd = new(() => new Currency(84, "BZD"), true);
    private static readonly Lazy<Currency> Xof = new(() => new Currency(952, "XOF"), true);
    private static readonly Lazy<Currency> Bmd = new(() => new Currency(60, "BMD"), true);
    private static readonly Lazy<Currency> Bgn = new(() => new Currency(975, "BGN"), true);
    private static readonly Lazy<Currency> Bob = new(() => new Currency(68, "BOB"), true);
    private static readonly Lazy<Currency> Bam = new(() => new Currency(977, "BAM"), true);
    private static readonly Lazy<Currency> Bwp = new(() => new Currency(72, "BWP"), true);
    private static readonly Lazy<Currency> Brl = new(() => new Currency(986, "BRL"), true);
    private static readonly Lazy<Currency> Bnd = new(() => new Currency(96, "BND"), true);
    private static readonly Lazy<Currency> Bif = new(() => new Currency(108, "BIF"), true);
    private static readonly Lazy<Currency> Btn = new(() => new Currency(64, "BTN"), true);
    private static readonly Lazy<Currency> Vuv = new(() => new Currency(548, "VUV"), true);
    private static readonly Lazy<Currency> Gbp = new(() => new Currency(826, "GBP"), true);
    private static readonly Lazy<Currency> Huf = new(() => new Currency(348, "HUF"), true);
    private static readonly Lazy<Currency> Veb = new(() => new Currency(862, "VEB"), true);
    private static readonly Lazy<Currency> Idr = new(() => new Currency(360, "IDR"), true);
    private static readonly Lazy<Currency> Vnd = new(() => new Currency(704, "VND"), true);
    private static readonly Lazy<Currency> Xaf = new(() => new Currency(950, "XAF"), true);
    private static readonly Lazy<Currency> Htg = new(() => new Currency(332, "HTG"), true);
    private static readonly Lazy<Currency> Gyd = new(() => new Currency(328, "GYD"), true);
    private static readonly Lazy<Currency> Gmd = new(() => new Currency(270, "GMD"), true);
    private static readonly Lazy<Currency> Ghc = new(() => new Currency(288, "GHC"), true);
    private static readonly Lazy<Currency> Gtq = new(() => new Currency(320, "GTQ"), true);
    private static readonly Lazy<Currency> Gnf = new(() => new Currency(324, "GNF"), true);
    private static readonly Lazy<Currency> Gip = new(() => new Currency(292, "GIP"), true);
    private static readonly Lazy<Currency> Hnl = new(() => new Currency(340, "HNL"), true);
    private static readonly Lazy<Currency> Hkd = new(() => new Currency(344, "HKD"), true);
    private static readonly Lazy<Currency> Gel = new(() => new Currency(981, "GEL"), true);
    private static readonly Lazy<Currency> Dkk = new(() => new Currency(208, "DKK"), true);
    private static readonly Lazy<Currency> Djf = new(() => new Currency(262, "DJF"), true);
    private static readonly Lazy<Currency> Dop = new(() => new Currency(214, "DOP"), true);
    private static readonly Lazy<Currency> Egp = new(() => new Currency(818, "EGP"), true);
    private static readonly Lazy<Currency> Zmk = new(() => new Currency(894, "ZMK"), true);
    private static readonly Lazy<Currency> Zwd = new(() => new Currency(716, "ZWD"), true);
    private static readonly Lazy<Currency> Ils = new(() => new Currency(376, "ILS"), true);
    private static readonly Lazy<Currency> Inr = new(() => new Currency(356, "INR"), true);
    private static readonly Lazy<Currency> Jod = new(() => new Currency(400, "JOD"), true);
    private static readonly Lazy<Currency> Iqd = new(() => new Currency(368, "IQD"), true);
    private static readonly Lazy<Currency> Irr = new(() => new Currency(364, "IRR"), true);
    private static readonly Lazy<Currency> Isk = new(() => new Currency(352, "ISK"), true);
    private static readonly Lazy<Currency> Yer = new(() => new Currency(886, "YER"), true);
    private static readonly Lazy<Currency> Cve = new(() => new Currency(132, "CVE"), true);
    private static readonly Lazy<Currency> Kzt = new(() => new Currency(398, "KZT"), true);
    private static readonly Lazy<Currency> Kyd = new(() => new Currency(136, "KYD"), true);
    private static readonly Lazy<Currency> Khr = new(() => new Currency(116, "KHR"), true);
    private static readonly Lazy<Currency> Cad = new(() => new Currency(124, "CAD"), true);
    private static readonly Lazy<Currency> Qar = new(() => new Currency(634, "QAR"), true);
    private static readonly Lazy<Currency> Kes = new(() => new Currency(404, "KES"), true);
    private static readonly Lazy<Currency> Cyp = new(() => new Currency(196, "CYP"), true);
    private static readonly Lazy<Currency> Kgs = new(() => new Currency(417, "KGS"), true);
    private static readonly Lazy<Currency> Cny = new(() => new Currency(156, "CNY"), true);
    private static readonly Lazy<Currency> Kpw = new(() => new Currency(408, "KPW"), true);
    private static readonly Lazy<Currency> Cop = new(() => new Currency(170, "COP"), true);
    private static readonly Lazy<Currency> Kmf = new(() => new Currency(174, "KMF"), true);
    private static readonly Lazy<Currency> Cdf = new(() => new Currency(976, "CDF"), true);
    private static readonly Lazy<Currency> Crc = new(() => new Currency(188, "CRC"), true);
    private static readonly Lazy<Currency> Cup = new(() => new Currency(192, "CUP"), true);
    private static readonly Lazy<Currency> Kwd = new(() => new Currency(414, "KWD"), true);
    private static readonly Lazy<Currency> Lak = new(() => new Currency(418, "LAK"), true);
    private static readonly Lazy<Currency> Lvl = new(() => new Currency(428, "LVL"), true);
    private static readonly Lazy<Currency> Lsl = new(() => new Currency(426, "LSL"), true);
    private static readonly Lazy<Currency> Zar = new(() => new Currency(710, "ZAR"), true);
    private static readonly Lazy<Currency> Lrd = new(() => new Currency(430, "LRD"), true);
    private static readonly Lazy<Currency> Lbp = new(() => new Currency(422, "LBP"), true);
    private static readonly Lazy<Currency> Lyd = new(() => new Currency(434, "LYD"), true);
    private static readonly Lazy<Currency> Ltl = new(() => new Currency(440, "LTL"), true);
    private static readonly Lazy<Currency> Chf = new(() => new Currency(756, "CHF"), true);
    private static readonly Lazy<Currency> Mur = new(() => new Currency(480, "MUR"), true);
    private static readonly Lazy<Currency> Mro = new(() => new Currency(478, "MRO"), true);
    private static readonly Lazy<Currency> Mga = new(() => new Currency(969, "MGA"), true);
    private static readonly Lazy<Currency> Mop = new(() => new Currency(446, "MOP"), true);
    private static readonly Lazy<Currency> Mkd = new(() => new Currency(807, "MKD"), true);
    private static readonly Lazy<Currency> Mwk = new(() => new Currency(454, "MWK"), true);
    private static readonly Lazy<Currency> Myr = new(() => new Currency(458, "MYR"), true);
    private static readonly Lazy<Currency> Mvr = new(() => new Currency(462, "MVR"), true);
    private static readonly Lazy<Currency> Mtl = new(() => new Currency(470, "MTL"), true);
    private static readonly Lazy<Currency> Mad = new(() => new Currency(504, "MAD"), true);
    private static readonly Lazy<Currency> Xdr = new(() => new Currency(960, "XDR"), true);
    private static readonly Lazy<Currency> Mxn = new(() => new Currency(484, "MXN"), true);
    private static readonly Lazy<Currency> Mzn = new(() => new Currency(943, "MZN"), true);
    private static readonly Lazy<Currency> Mdl = new(() => new Currency(498, "MDL"), true);
    private static readonly Lazy<Currency> Mnt = new(() => new Currency(496, "MNT"), true);
    private static readonly Lazy<Currency> Mmk = new(() => new Currency(104, "MMK"), true);
    private static readonly Lazy<Currency> Nad = new(() => new Currency(516, "NAD"), true);
    private static readonly Lazy<Currency> Npr = new(() => new Currency(524, "NPR"), true);
    private static readonly Lazy<Currency> Ngn = new(() => new Currency(566, "NGN"), true);
    private static readonly Lazy<Currency> Ang = new(() => new Currency(532, "ANG"), true);
    private static readonly Lazy<Currency> Nio = new(() => new Currency(558, "NIO"), true);
    private static readonly Lazy<Currency> Nzd = new(() => new Currency(554, "NZD"), true);
    private static readonly Lazy<Currency> Nok = new(() => new Currency(578, "NOK"), true);
    private static readonly Lazy<Currency> Aed = new(() => new Currency(784, "AED"), true);
    private static readonly Lazy<Currency> Omr = new(() => new Currency(512, "OMR"), true);
    private static readonly Lazy<Currency> Shp = new(() => new Currency(654, "SHP"), true);
    private static readonly Lazy<Currency> Pkr = new(() => new Currency(586, "PKR"), true);
    private static readonly Lazy<Currency> Pab = new(() => new Currency(590, "PAB"), true);
    private static readonly Lazy<Currency> Pgk = new(() => new Currency(598, "PGK"), true);
    private static readonly Lazy<Currency> Pyg = new(() => new Currency(600, "PYG"), true);
    private static readonly Lazy<Currency> Pen = new(() => new Currency(604, "PEN"), true);
    private static readonly Lazy<Currency> Pln = new(() => new Currency(985, "PLN"), true);
    private static readonly Lazy<Currency> Rub = new(() => new Currency(643, "RUB"), true);
    private static readonly Lazy<Currency> Rwf = new(() => new Currency(646, "RWF"), true);
    private static readonly Lazy<Currency> Ron = new(() => new Currency(946, "RON"), true);
    private static readonly Lazy<Currency> Wst = new(() => new Currency(882, "WST"), true);
    private static readonly Lazy<Currency> Std = new(() => new Currency(678, "STD"), true);
    private static readonly Lazy<Currency> Sar = new(() => new Currency(682, "SAR"), true);
    private static readonly Lazy<Currency> Szl = new(() => new Currency(748, "SZL"), true);
    private static readonly Lazy<Currency> Scr = new(() => new Currency(690, "SCR"), true);
    private static readonly Lazy<Currency> Csd = new(() => new Currency(891, "CSD"), true);
    private static readonly Lazy<Currency> Sgd = new(() => new Currency(702, "SGD"), true);
    private static readonly Lazy<Currency> Syp = new(() => new Currency(760, "SYP"), true);
    private static readonly Lazy<Currency> Skk = new(() => new Currency(703, "SKK"), true);
    private static readonly Lazy<Currency> Sit = new(() => new Currency(705, "SIT"), true);
    private static readonly Lazy<Currency> Sbd = new(() => new Currency(90, "SBD"), true);
    private static readonly Lazy<Currency> Sos = new(() => new Currency(706, "SOS"), true);
    private static readonly Lazy<Currency> Sdd = new(() => new Currency(736, "SDD"), true);
    private static readonly Lazy<Currency> Srd = new(() => new Currency(968, "SRD"), true);
    private static readonly Lazy<Currency> Usd = new(() => new Currency(840, "USD"), true);
    private static readonly Lazy<Currency> Sll = new(() => new Currency(694, "SLL"), true);
    private static readonly Lazy<Currency> Tjs = new(() => new Currency(972, "TJS"), true);
    private static readonly Lazy<Currency> Thb = new(() => new Currency(764, "THB"), true);
    private static readonly Lazy<Currency> Twd = new(() => new Currency(901, "TWD"), true);
    private static readonly Lazy<Currency> Tzs = new(() => new Currency(834, "TZS"), true);
    private static readonly Lazy<Currency> Top = new(() => new Currency(776, "TOP"), true);
    private static readonly Lazy<Currency> Ttd = new(() => new Currency(780, "TTD"), true);
    private static readonly Lazy<Currency> Tnd = new(() => new Currency(788, "TND"), true);
    private static readonly Lazy<Currency> Tmm = new(() => new Currency(795, "TMM"), true);
    private static readonly Lazy<Currency> Try = new(() => new Currency(949, "TRY"), true);
    private static readonly Lazy<Currency> Ugx = new(() => new Currency(800, "UGX"), true);
    private static readonly Lazy<Currency> Uzs = new(() => new Currency(860, "UZS"), true);
    private static readonly Lazy<Currency> Uah = new(() => new Currency(980, "UAH"), true);
    private static readonly Lazy<Currency> Uyu = new(() => new Currency(858, "UYU"), true);
    private static readonly Lazy<Currency> Fjd = new(() => new Currency(242, "FJD"), true);
    private static readonly Lazy<Currency> Php = new(() => new Currency(608, "PHP"), true);
    private static readonly Lazy<Currency> Fkp = new(() => new Currency(238, "FKP"), true);
    private static readonly Lazy<Currency> Xpf = new(() => new Currency(953, "XPF"), true);
    private static readonly Lazy<Currency> Hrk = new(() => new Currency(191, "HRK"), true);
    private static readonly Lazy<Currency> Czk = new(() => new Currency(203, "CZK"), true);
    private static readonly Lazy<Currency> Clp = new(() => new Currency(152, "CLP"), true);
    private static readonly Lazy<Currency> Sek = new(() => new Currency(752, "SEK"), true);
    private static readonly Lazy<Currency> Lkr = new(() => new Currency(144, "LKR"), true);
    private static readonly Lazy<Currency> Ern = new(() => new Currency(232, "ERN"), true);
    private static readonly Lazy<Currency> Eek = new(() => new Currency(233, "EEK"), true);
    private static readonly Lazy<Currency> Etb = new(() => new Currency(230, "ETB"), true);
    //private static readonly Lazy<Currency> Yum = new(() => new Currency(891, "YUM"), true);
    private static readonly Lazy<Currency> Krw = new(() => new Currency(410, "KRW"), true);
    private static readonly Lazy<Currency> Jmd = new(() => new Currency(388, "JMD"), true);
    private static readonly Lazy<Currency> Jpy = new(() => new Currency(392, "JPY"), true);
    private static readonly Lazy<Currency> Xag = new(() => new Currency(961, "XAG"), true);
    private static readonly Lazy<Currency> Xau = new(() => new Currency(959, "XAU"), true);
    private static readonly Lazy<Currency> Xba = new(() => new Currency(955, "XBA"), true);
    private static readonly Lazy<Currency> Xbb = new(() => new Currency(956, "XBB"), true);
    private static readonly Lazy<Currency> Xbc = new(() => new Currency(957, "XBC"), true);
    private static readonly Lazy<Currency> Xbd = new(() => new Currency(958, "XBD"), true);
    //private static readonly Lazy<Currency> Xfo = new(() => new Currency(0, "XFO"), true);
    //private static readonly Lazy<Currency> Xfu = new(() => new Currency(0, "XFU"), true);
    private static readonly Lazy<Currency> Xpd = new(() => new Currency(964, "XPD"), true);
    private static readonly Lazy<Currency> Xpt = new(() => new Currency(962, "XPT"), true);
    private static readonly Lazy<Currency> Xts = new(() => new Currency(963, "XTS"), true);
    private static readonly Lazy<Currency> Xxx = new(() => new Currency(999, "XXX"), true);

    /// <summary>
    /// Цифровой код валюты.
    /// </summary>
    public ushort Code
    {
        get => code;
        set => SetProperty(ref code, value);
    }

    /// <summary>
    /// Символьный код валюты.
    /// </summary>
    public string IsoCode
    {
        get => isoCode;
        set => SetProperty(ref isoCode, value);
    }

    /// <summary>
    /// Происходит в момент изменения цифрового кода валюты.
    /// </summary>
    public event AsyncEventHandler<Currency>? CodeChanged;

    /// <summary>
    /// Происходит в момент изменения символьного кода валюты.
    /// </summary>
    public event AsyncEventHandler<Currency>? IsoCodeChanged;

    /// <summary>
    /// Австралийский доллар.
    /// </summary>
    public static Currency AUD => Aud.Value;

    /// <summary>
    /// Евро.
    /// </summary>
    public static Currency EUR => Eur.Value;

    /// <summary>
    /// Азербайджанский манат.
    /// </summary>
    public static Currency AZN => Azn.Value;

    /// <summary>
    /// Лек.
    /// </summary>
    public static Currency ALL => All.Value;

    /// <summary>
    /// Алжирский динар.
    /// </summary>
    public static Currency DZD => Dzd.Value;

    /// <summary>
    /// Восточно-карибский доллар.
    /// </summary>
    public static Currency XCD => Xcd.Value;

    /// <summary>
    /// Кванза.
    /// </summary>
    public static Currency AOA => Aoa.Value;

    /// <summary>
    /// Аргентинское песо.
    /// </summary>
    public static Currency ARS => Ars.Value;

    /// <summary>
    /// Армянский драм.
    /// </summary>
    public static Currency AMD => Amd.Value;

    /// <summary>
    /// Арубанский гульден.
    /// </summary>
    public static Currency AWG => Awg.Value;

    /// <summary>
    /// Афгани.
    /// </summary>
    public static Currency AFN => Afn.Value;

    /// <summary>
    /// Багамский доллар.
    /// </summary>
    public static Currency BSD => Bsd.Value;

    /// <summary>
    /// Така.
    /// </summary>
    public static Currency BDT => Bdt.Value;

    /// <summary>
    /// Барбадосский доллар.
    /// </summary>
    public static Currency BBD => Bbd.Value;

    /// <summary>
    /// Бахрейнский динар.
    /// </summary>
    public static Currency BHD => Bhd.Value;

    /// <summary>
    /// Белорусский рубль.
    /// </summary>
    public static Currency BYR => Byr.Value;

    /// <summary>
    /// Белизский доллар.
    /// </summary>
    public static Currency BZD => Bzd.Value;

    /// <summary>
    /// Франк КФА ВСЕАО (денежная единица Центрального Банка государств Западной Африки).
    /// </summary>
    public static Currency XOF => Xof.Value;

    /// <summary>
    /// Бермудский доллар.
    /// </summary>
    public static Currency BMD => Bmd.Value;

    /// <summary>
    /// Лев.
    /// </summary>
    public static Currency BGN => Bgn.Value;

    /// <summary>
    /// Боливиано.
    /// </summary>
    public static Currency BOB => Bob.Value;

    /// <summary>
    /// Конвертируемая марка.
    /// </summary>
    public static Currency BAM => Bam.Value;

    /// <summary>
    /// Пула.
    /// </summary>
    public static Currency BWP => Bwp.Value;

    /// <summary>
    /// Бразильский реал.
    /// </summary>
    public static Currency BRL => Brl.Value;

    /// <summary>
    /// Брунейский доллар.
    /// </summary>
    public static Currency BND => Bnd.Value;

    /// <summary>
    /// Бурундийский франк.
    /// </summary>
    public static Currency BIF => Bif.Value;

    /// <summary>
    /// Нгултрум.
    /// </summary>
    public static Currency BTN => Btn.Value;

    /// <summary>
    /// Вату.
    /// </summary>
    public static Currency VUV => Vuv.Value;

    /// <summary>
    /// Фунт стерлингов.
    /// </summary>
    public static Currency GBP => Gbp.Value;

    /// <summary>
    /// Форинт.
    /// </summary>
    public static Currency HUF => Huf.Value;

    /// <summary>
    /// Боливар.
    /// </summary>
    public static Currency VEB => Veb.Value;

    /// <summary>
    /// Рупия.
    /// </summary>
    public static Currency IDR => Idr.Value;

    /// <summary>
    /// Донг.
    /// </summary>
    public static Currency VND => Vnd.Value;

    /// <summary>
    /// Франк КФА ВЕАС (денежная единица Банка государств Центральной Африки).
    /// </summary>
    public static Currency XAF => Xaf.Value;

    /// <summary>
    /// Гурд.
    /// </summary>
    public static Currency HTG => Htg.Value;

    /// <summary>
    /// Гайанский доллар.
    /// </summary>
    public static Currency GYD => Gyd.Value;

    /// <summary>
    /// Даласи.
    /// </summary>
    public static Currency GMD => Gmd.Value;

    /// <summary>
    /// Седи.
    /// </summary>
    public static Currency GHC => Ghc.Value;

    /// <summary>
    /// Кетсаль.
    /// </summary>
    public static Currency GTQ => Gtq.Value;

    /// <summary>
    /// Гвинейский франк.
    /// </summary>
    public static Currency GNF => Gnf.Value;

    /// <summary>
    /// Гибралтарский фунт.
    /// </summary>
    public static Currency GIP => Gip.Value;

    /// <summary>
    /// Лемпира.
    /// </summary>
    public static Currency HNL => Hnl.Value;

    /// <summary>
    /// Гонконгский доллар.
    /// </summary>
    public static Currency HKD => Hkd.Value;

    /// <summary>
    /// Лари.
    /// </summary>
    public static Currency GEL => Gel.Value;

    /// <summary>
    /// Датская крона.
    /// </summary>
    public static Currency DKK => Dkk.Value;

    /// <summary>
    /// Франк Джибути.
    /// </summary>
    public static Currency DJF => Djf.Value;

    /// <summary>
    /// Доминиканское песо.
    /// </summary>
    public static Currency DOP => Dop.Value;

    /// <summary>
    /// Египетский фунт.
    /// </summary>
    public static Currency EGP => Egp.Value;

    /// <summary>
    /// Квача (замбийская).
    /// </summary>
    public static Currency ZMK => Zmk.Value;

    /// <summary>
    /// Доллар Зимбабве.
    /// </summary>
    public static Currency ZWD => Zwd.Value;

    /// <summary>
    /// Новый израильский шекель.
    /// </summary>
    public static Currency ILS => Ils.Value;

    /// <summary>
    /// Индийская рупия.
    /// </summary>
    public static Currency INR => Inr.Value;

    /// <summary>
    /// Иорданский динар.
    /// </summary>
    public static Currency JOD => Jod.Value;

    /// <summary>
    /// Иракский динар.
    /// </summary>
    public static Currency IQD => Iqd.Value;

    /// <summary>
    /// Иранский риал.
    /// </summary>
    public static Currency IRR => Irr.Value;

    /// <summary>
    /// Исландская крона.
    /// </summary>
    public static Currency ISK => Isk.Value;

    /// <summary>
    /// Йеменский риал.
    /// </summary>
    public static Currency YER => Yer.Value;

    /// <summary>
    /// Эскудо Кабо-Верде.
    /// </summary>
    public static Currency CVE => Cve.Value;

    /// <summary>
    /// Тенге.
    /// </summary>
    public static Currency KZT => Kzt.Value;

    /// <summary>
    /// Доллар Каймановых островов.
    /// </summary>
    public static Currency KYD => Kyd.Value;

    /// <summary>
    /// Риель.
    /// </summary>
    public static Currency KHR => Khr.Value;

    /// <summary>
    /// Канадский доллар.
    /// </summary>
    public static Currency CAD => Cad.Value;

    /// <summary>
    /// Катарский риал.
    /// </summary>
    public static Currency QAR => Qar.Value;

    /// <summary>
    /// Кенийский шиллинг.
    /// </summary>
    public static Currency KES => Kes.Value;

    /// <summary>
    /// Кипрский фунт.
    /// </summary>
    public static Currency CYP => Cyp.Value;

    /// <summary>
    /// Сом.
    /// </summary>
    public static Currency KGS => Kgs.Value;

    /// <summary>
    /// Юань жэньминьби.
    /// </summary>
    public static Currency CNY => Cny.Value;

    /// <summary>
    /// Северо-корейская вона.
    /// </summary>
    public static Currency KPW => Kpw.Value;

    /// <summary>
    /// Колумбийское песо.
    /// </summary>
    public static Currency COP => Cop.Value;

    /// <summary>
    /// Франк Коморских островов.
    /// </summary>
    public static Currency KMF => Kmf.Value;

    /// <summary>
    /// Конголезский франк.
    /// </summary>
    public static Currency CDF => Cdf.Value;

    /// <summary>
    /// Костариканский колон.
    /// </summary>
    public static Currency CRC => Crc.Value;

    /// <summary>
    /// Кубинское песо.
    /// </summary>
    public static Currency CUP => Cup.Value;

    /// <summary>
    /// Кувейтский динар.
    /// </summary>
    public static Currency KWD => Kwd.Value;

    /// <summary>
    /// Кип.
    /// </summary>
    public static Currency LAK => Lak.Value;

    /// <summary>
    /// Латвийский лат.
    /// </summary>
    public static Currency LVL => Lvl.Value;

    /// <summary>
    /// Лоти.
    /// </summary>
    public static Currency LSL => Lsl.Value;

    /// <summary>
    /// Рэнд.
    /// </summary>
    public static Currency ZAR => Zar.Value;

    /// <summary>
    /// Либерийский доллар.
    /// </summary>
    public static Currency LRD => Lrd.Value;

    /// <summary>
    /// Ливанский фунт.
    /// </summary>
    public static Currency LBP => Lbp.Value;

    /// <summary>
    /// Ливийский динар.
    /// </summary>
    public static Currency LYD => Lyd.Value;

    /// <summary>
    /// Литовский лит.
    /// </summary>
    public static Currency LTL => Ltl.Value;

    /// <summary>
    /// Швейцарский франк.
    /// </summary>
    public static Currency CHF => Chf.Value;

    /// <summary>
    /// Маврикийская рупия.
    /// </summary>
    public static Currency MUR => Mur.Value;

    /// <summary>
    /// Угия.
    /// </summary>
    public static Currency MRO => Mro.Value;

    /// <summary>
    /// Малагасийский франк.
    /// </summary>
    public static Currency MGA => Mga.Value;

    /// <summary>
    /// Патака.
    /// </summary>
    public static Currency MOP => Mop.Value;

    /// <summary>
    /// Динар.
    /// </summary>
    public static Currency MKD => Mkd.Value;

    /// <summary>
    /// Квача.
    /// </summary>
    public static Currency MWK => Mwk.Value;

    /// <summary>
    /// Малайзийский рингтит.
    /// </summary>
    public static Currency MYR => Myr.Value;

    /// <summary>
    /// Руфия.
    /// </summary>
    public static Currency MVR => Mvr.Value;

    /// <summary>
    /// Мальтийская лира.
    /// </summary>
    public static Currency MTL => Mtl.Value;

    /// <summary>
    /// Марокканский дирхам.
    /// </summary>
    public static Currency MAD => Mad.Value;

    /// <summary>
    /// СДР (специальные права заимствования).
    /// </summary>
    public static Currency XDR => Xdr.Value;

    /// <summary>
    /// Мексиканское песо.
    /// </summary>
    public static Currency MXN => Mxn.Value;

    /// <summary>
    /// Метикал.
    /// </summary>
    public static Currency MZN => Mzn.Value;

    /// <summary>
    /// Молдавский лей.
    /// </summary>
    public static Currency MDL => Mdl.Value;

    /// <summary>
    /// Тугрик.
    /// </summary>
    public static Currency MNT => Mnt.Value;

    /// <summary>
    /// Кьят.
    /// </summary>
    public static Currency MMK => Mmk.Value;

    /// <summary>
    /// Доллар Намибии.
    /// </summary>
    public static Currency NAD => Nad.Value;

    /// <summary>
    /// Непальская рупия.
    /// </summary>
    public static Currency NPR => Npr.Value;

    /// <summary>
    /// Найра.
    /// </summary>
    public static Currency NGN => Ngn.Value;

    /// <summary>
    /// Нидерландский антильский гульден.
    /// </summary>
    public static Currency ANG => Ang.Value;

    /// <summary>
    /// Золотая кордоба.
    /// </summary>
    public static Currency NIO => Nio.Value;

    /// <summary>
    /// Новозеландский доллар.
    /// </summary>
    public static Currency NZD => Nzd.Value;

    /// <summary>
    /// Норвежская крона.
    /// </summary>
    public static Currency NOK => Nok.Value;

    /// <summary>
    /// Дирхам (ОАЭ).
    /// </summary>
    public static Currency AED => Aed.Value;

    /// <summary>
    /// Оманский риал.
    /// </summary>
    public static Currency OMR => Omr.Value;

    /// <summary>
    /// Фунт Острова Святой Елены.
    /// </summary>
    public static Currency SHP => Shp.Value;

    /// <summary>
    /// Пакистанская рупия.
    /// </summary>
    public static Currency PKR => Pkr.Value;

    /// <summary>
    /// Бальбоа.
    /// </summary>
    public static Currency PAB => Pab.Value;

    /// <summary>
    /// Кина.
    /// </summary>
    public static Currency PGK => Pgk.Value;

    /// <summary>
    /// Гуарани.
    /// </summary>
    public static Currency PYG => Pyg.Value;

    /// <summary>
    /// Новый соль.
    /// </summary>
    public static Currency PEN => Pen.Value;

    /// <summary>
    /// Злотый.
    /// </summary>
    public static Currency PLN => Pln.Value;

    /// <summary>
    /// Российский рубль.
    /// </summary>
    public static Currency RUB => Rub.Value;

    /// <summary>
    /// Франк Руанды.
    /// </summary>
    public static Currency RWF => Rwf.Value;

    /// <summary>
    /// Лей.
    /// </summary>
    public static Currency RON => Ron.Value;

    /// <summary>
    /// Тала.
    /// </summary>
    public static Currency WST => Wst.Value;

    /// <summary>
    /// Добра.
    /// </summary>
    public static Currency STD => Std.Value;

    /// <summary>
    /// Саудовский риял.
    /// </summary>
    public static Currency SAR => Sar.Value;

    /// <summary>
    /// Лилангени.
    /// </summary>
    public static Currency SZL => Szl.Value;

    /// <summary>
    /// Сейшельская рупия.
    /// </summary>
    public static Currency SCR => Scr.Value;

    /// <summary>
    /// Сербский динар.
    /// </summary>
    public static Currency CSD => Csd.Value;

    /// <summary>
    /// Сингапурский доллар.
    /// </summary>
    public static Currency SGD => Sgd.Value;

    /// <summary>
    /// Сирийский фунт.
    /// </summary>
    public static Currency SYP => Syp.Value;

    /// <summary>
    /// Словацкая крона.
    /// </summary>
    public static Currency SKK => Skk.Value;

    /// <summary>
    /// Толар.
    /// </summary>
    public static Currency SIT => Sit.Value;

    /// <summary>
    /// Доллар Соломоновых островов.
    /// </summary>
    public static Currency SBD => Sbd.Value;

    /// <summary>
    /// Сомалийский шиллинг.
    /// </summary>
    public static Currency SOS => Sos.Value;

    /// <summary>
    /// Суданский динар.
    /// </summary>
    public static Currency SDD => Sdd.Value;

    /// <summary>
    /// Суринамский доллар.
    /// </summary>
    public static Currency SRD => Srd.Value;

    /// <summary>
    /// Доллар США.
    /// </summary>
    public static Currency USD => Usd.Value;

    /// <summary>
    /// Леоне.
    /// </summary>
    public static Currency SLL => Sll.Value;

    /// <summary>
    /// Сомони.
    /// </summary>
    public static Currency TJS => Tjs.Value;

    /// <summary>
    /// Бат.
    /// </summary>
    public static Currency THB => Thb.Value;

    /// <summary>
    /// Тайваньский доллар.
    /// </summary>
    public static Currency TWD => Twd.Value;

    /// <summary>
    /// Танзанийский шиллинг.
    /// </summary>
    public static Currency TZS => Tzs.Value;

    /// <summary>
    /// Паанга.
    /// </summary>
    public static Currency TOP => Top.Value;

    /// <summary>
    /// Доллар Тринидада и Тобаго.
    /// </summary>
    public static Currency TTD => Ttd.Value;

    /// <summary>
    /// Тунисский динар.
    /// </summary>
    public static Currency TND => Tnd.Value;

    /// <summary>
    /// Манат.
    /// </summary>
    public static Currency TMM => Tmm.Value;

    /// <summary>
    /// Турецкая лира.
    /// </summary>
    public static Currency TRY => Try.Value;

    /// <summary>
    /// Угандийский шиллинг.
    /// </summary>
    public static Currency UGX => Ugx.Value;

    /// <summary>
    /// Узбекский сум.
    /// </summary>
    public static Currency UZS => Uzs.Value;

    /// <summary>
    /// Гривна.
    /// </summary>
    public static Currency UAH => Uah.Value;

    /// <summary>
    /// Уругвайское песо.
    /// </summary>
    public static Currency UYU => Uyu.Value;

    /// <summary>
    /// Доллар Фиджи.
    /// </summary>
    public static Currency FJD => Fjd.Value;

    /// <summary>
    /// Филиппинское песо.
    /// </summary>
    public static Currency PHP => Php.Value;

    /// <summary>
    /// Фунт Фолклендских островов.
    /// </summary>
    public static Currency FKP => Fkp.Value;

    /// <summary>
    /// Франк КФП.
    /// </summary>
    public static Currency XPF => Xpf.Value;

    /// <summary>
    /// Куна.
    /// </summary>
    public static Currency HRK => Hrk.Value;

    /// <summary>
    /// Чешская крона.
    /// </summary>
    public static Currency CZK => Czk.Value;

    /// <summary>
    /// Чилийское песо.
    /// </summary>
    public static Currency CLP => Clp.Value;

    /// <summary>
    /// Шведская крона.
    /// </summary>
    public static Currency SEK => Sek.Value;

    /// <summary>
    /// Шри-Ланкийская рупия.
    /// </summary>
    public static Currency LKR => Lkr.Value;

    /// <summary>
    /// Накфа.
    /// </summary>
    public static Currency ERN => Ern.Value;

    /// <summary>
    /// Эстонская крона.
    /// </summary>
    public static Currency EEK => Eek.Value;

    /// <summary>
    /// Эфиопский быр.
    /// </summary>
    public static Currency ETB => Etb.Value;

    ///// <summary>
    ///// Югославский динар.
    ///// </summary>
    //public static Currency YUM => Yum.Value;

    /// <summary>
    /// Вона.
    /// </summary>
    public static Currency KRW => Krw.Value;

    /// <summary>
    /// Ямайский доллар.
    /// </summary>
    public static Currency JMD => Jmd.Value;

    /// <summary>
    /// Иена.
    /// </summary>
    public static Currency JPY => Jpy.Value;

    /// <summary>
    /// Тройская унция серебра.
    /// </summary>
    public static Currency XAG => Xag.Value;

    /// <summary>
    /// Тройская унция золота.
    /// </summary>
    public static Currency XAU => Xau.Value;

    /// <summary>
    /// Европейская составная единица EURCO.
    /// </summary>
    public static Currency XBA => Xba.Value;

    /// <summary>
    /// Европейская валютная единица EMU-6.
    /// </summary>
    public static Currency XBB => Xbb.Value;

    /// <summary>
    /// Расчётная единица Европейского платежного союза EUA-9.
    /// </summary>
    public static Currency XBC => Xbc.Value;

    /// <summary>
    /// Расчётная единица Европейского платежного союза EUA-17.
    /// </summary>
    public static Currency XBD => Xbd.Value;

    ///// <summary>
    ///// Франк золотой (Специальная валюта для расчётов).
    ///// </summary>
    //public static Currency XFO => Xfo.Value;

    ///// <summary>
    ///// Франк ЮИК (Специальная валюта для расчётов).
    ///// </summary>
    //public static Currency XFU => Xfu.Value;

    /// <summary>
    /// Тройская унция палладия.
    /// </summary>
    public static Currency XPD => Xpd.Value;

    /// <summary>
    /// Тройская унция платины.
    /// </summary>
    public static Currency XPT => Xpt.Value;

    /// <summary>
    /// Код зарезервированный для тестовых целей.
    /// </summary>
    public static Currency XTS => Xts.Value;

    /// <summary>
    /// Отсутствие валюты.
    /// </summary>
    public static Currency XXX => Xxx.Value;

    /// <inheritdoc />
    protected override async void OnPropertyChanged(string? propertyName = default)
    {
        base.OnPropertyChanged(propertyName);

        switch (propertyName)
        {
            case "Code": await CodeChanged.On(this).ConfigureAwait(false); break;
            case "IsoCode": await IsoCodeChanged.On(this).ConfigureAwait(false); break;
        }
    }

    /// <summary>
    /// Преобразует текущий экземпляр <see cref="Currency"/> в символьный код валюты.
    /// </summary>
    /// <returns>Символьный код валюты.</returns>
    public override string ToString() => IsoCode;

    /// <summary>
    /// Преобразует текущий экземпляр <see cref="Currency"/> в цифровой код валюты.
    /// </summary>
    /// <returns>Цифровой код валюты.</returns>
    public virtual ushort ToUInt16() => Code;

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его символьному коду.
    /// </summary>
    /// <param name="s">Символьный код валюты.</param>
    /// <param name="provider">Параметры форматирования.</param>
    /// <param name="result">Экземпляр <see cref="Currency"/>.</param>
    /// <returns><c>True</c>, если экземпляр был найден, иначе <c>false</c>.</returns>
    public static bool TryParse(string? s, IFormatProvider? provider, out Currency? result)
    {
        result = s?.ToUpperInvariant() switch
        {
            "AUD" => AUD,
            "EUR" => EUR,
            "AZN" => AZN,
            "ALL" => ALL,
            "DZD" => DZD,
            "XCD" => XCD,
            "AOA" => AOA,
            "ARS" => ARS,
            "AMD" => AMD,
            "AWG" => AWG,
            "AFN" => AFN,
            "BSD" => BSD,
            "BDT" => BDT,
            "BBD" => BBD,
            "BHD" => BHD,
            "BYR" => BYR,
            "BZD" => BZD,
            "XOF" => XOF,
            "BMD" => BMD,
            "BGN" => BGN,
            "BOB" => BOB,
            "BAM" => BAM,
            "BWP" => BWP,
            "BRL" => BRL,
            "BND" => BND,
            "BIF" => BIF,
            "BTN" => BTN,
            "VUV" => VUV,
            "GBP" => GBP,
            "HUF" => HUF,
            "VEB" => VEB,
            "IDR" => IDR,
            "VND" => VND,
            "XAF" => XAF,
            "HTG" => HTG,
            "GYD" => GYD,
            "GMD" => GMD,
            "GHC" => GHC,
            "GTQ" => GTQ,
            "GNF" => GNF,
            "GIP" => GIP,
            "HNL" => HNL,
            "HKD" => HKD,
            "GEL" => GEL,
            "DKK" => DKK,
            "DJF" => DJF,
            "DOP" => DOP,
            "EGP" => EGP,
            "ZMK" => ZMK,
            "ZWD" => ZWD,
            "ILS" => ILS,
            "INR" => INR,
            "JOD" => JOD,
            "IQD" => IQD,
            "IRR" => IRR,
            "ISK" => ISK,
            "YER" => YER,
            "CVE" => CVE,
            "KZT" => KZT,
            "KYD" => KYD,
            "KHR" => KHR,
            "CAD" => CAD,
            "QAR" => QAR,
            "KES" => KES,
            "CYP" => CYP,
            "KGS" => KGS,
            "CNY" => CNY,
            "KPW" => KPW,
            "COP" => COP,
            "KMF" => KMF,
            "CDF" => CDF,
            "CRC" => CRC,
            "CUP" => CUP,
            "KWD" => KWD,
            "LAK" => LAK,
            "LVL" => LVL,
            "LSL" => LSL,
            "ZAR" => ZAR,
            "LRD" => LRD,
            "LBP" => LBP,
            "LYD" => LYD,
            "LTL" => LTL,
            "CHF" => CHF,
            "MUR" => MUR,
            "MRO" => MRO,
            "MGA" => MGA,
            "MOP" => MOP,
            "MKD" => MKD,
            "MWK" => MWK,
            "MYR" => MYR,
            "MVR" => MVR,
            "MTL" => MTL,
            "MAD" => MAD,
            "XDR" => XDR,
            "MXN" => MXN,
            "MZN" => MZN,
            "MDL" => MDL,
            "MNT" => MNT,
            "MMK" => MMK,
            "NAD" => NAD,
            "NPR" => NPR,
            "NGN" => NGN,
            "ANG" => ANG,
            "NIO" => NIO,
            "NZD" => NZD,
            "NOK" => NOK,
            "AED" => AED,
            "OMR" => OMR,
            "SHP" => SHP,
            "PKR" => PKR,
            "PAB" => PAB,
            "PGK" => PGK,
            "PYG" => PYG,
            "PEN" => PEN,
            "PLN" => PLN,
            "RUB" => RUB,
            "RWF" => RWF,
            "RON" => RON,
            "WST" => WST,
            "STD" => STD,
            "SAR" => SAR,
            "SZL" => SZL,
            "SCR" => SCR,
            "CSD" => CSD,
            "SGD" => SGD,
            "SYP" => SYP,
            "SKK" => SKK,
            "SIT" => SIT,
            "SBD" => SBD,
            "SOS" => SOS,
            "SDD" => SDD,
            "SRD" => SRD,
            "USD" => USD,
            "SLL" => SLL,
            "TJS" => TJS,
            "THB" => THB,
            "TWD" => TWD,
            "TZS" => TZS,
            "TOP" => TOP,
            "TTD" => TTD,
            "TND" => TND,
            "TMM" => TMM,
            "TRY" => TRY,
            "UGX" => UGX,
            "UZS" => UZS,
            "UAH" => UAH,
            "UYU" => UYU,
            "FJD" => FJD,
            "PHP" => PHP,
            "FKP" => FKP,
            "XPF" => XPF,
            "HRK" => HRK,
            "CZK" => CZK,
            "CLP" => CLP,
            "SEK" => SEK,
            "LKR" => LKR,
            "ERN" => ERN,
            "EEK" => EEK,
            "ETB" => ETB,
            //"YUM" => YUM,
            "KRW" => KRW,
            "JMD" => JMD,
            "JPY" => JPY,
            "XAG" => XAG,
            "XAU" => XAU,
            "XBA" => XBA,
            "XBB" => XBB,
            "XBC" => XBC,
            "XBD" => XBD,
            //"XFO" => XFO,
            //"XFU" => XFU,
            "XPD" => XPD,
            "XPT" => XPT,
            "XTS" => XTS,
            "XXX" => XXX,

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
    public static bool TryParse(string? s, out Currency? result) => TryParse(s, default, out result);

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его символьному коду. 
    /// </summary>
    /// <param name="s">Символьный код валюты.</param>
    /// <param name="provider">Параметры форматирования.</param>
    /// <returns>Экземпляр <see cref="Currency"/>.</returns>
    /// <exception cref="FormatException" />
    public static Currency Parse(string s, IFormatProvider? provider) => !TryParse(s, provider, out var result) || result is null ? throw new FormatException() : result;

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его символьному коду. 
    /// </summary>
    /// <param name="s">Символьный код валюты.</param>
    /// <returns>Экземпляр <see cref="Currency"/>.</returns>
    /// <exception cref="FormatException" />
    public static Currency Parse(string s) => Parse(s, default);

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его цифровому коду.
    /// </summary>
    /// <param name="code">Цифровой код валюты.</param>
    /// <param name="currency">Экземпляр <see cref="Currency"/>.</param>
    /// <returns><c>True</c>, если экземпляр был найден, иначе <c>false</c>.</returns>
    public static bool TryParse(ushort code, out Currency? currency)
    {
        currency = code switch
        {
            36 => AUD,
            978 => EUR,
            944 => AZN,
            8 => ALL,
            12 => DZD,
            951 => XCD,
            973 => AOA,
            32 => ARS,
            51 => AMD,
            533 => AWG,
            971 => AFN,
            44 => BSD,
            50 => BDT,
            52 => BBD,
            48 => BHD,
            974 => BYR,
            84 => BZD,
            952 => XOF,
            60 => BMD,
            975 => BGN,
            68 => BOB,
            977 => BAM,
            72 => BWP,
            986 => BRL,
            96 => BND,
            108 => BIF,
            64 => BTN,
            548 => VUV,
            826 => GBP,
            348 => HUF,
            862 => VEB,
            360 => IDR,
            704 => VND,
            950 => XAF,
            332 => HTG,
            328 => GYD,
            270 => GMD,
            288 => GHC,
            320 => GTQ,
            324 => GNF,
            292 => GIP,
            340 => HNL,
            344 => HKD,
            981 => GEL,
            208 => DKK,
            262 => DJF,
            214 => DOP,
            818 => EGP,
            894 => ZMK,
            716 => ZWD,
            376 => ILS,
            356 => INR,
            400 => JOD,
            368 => IQD,
            364 => IRR,
            352 => ISK,
            886 => YER,
            132 => CVE,
            398 => KZT,
            136 => KYD,
            116 => KHR,
            124 => CAD,
            634 => QAR,
            404 => KES,
            196 => CYP,
            417 => KGS,
            156 => CNY,
            408 => KPW,
            170 => COP,
            174 => KMF,
            976 => CDF,
            188 => CRC,
            192 => CUP,
            414 => KWD,
            418 => LAK,
            428 => LVL,
            426 => LSL,
            710 => ZAR,
            430 => LRD,
            422 => LBP,
            434 => LYD,
            440 => LTL,
            756 => CHF,
            480 => MUR,
            478 => MRO,
            969 => MGA,
            446 => MOP,
            807 => MKD,
            454 => MWK,
            458 => MYR,
            462 => MVR,
            470 => MTL,
            504 => MAD,
            960 => XDR,
            484 => MXN,
            943 => MZN,
            498 => MDL,
            496 => MNT,
            104 => MMK,
            516 => NAD,
            524 => NPR,
            566 => NGN,
            532 => ANG,
            558 => NIO,
            554 => NZD,
            578 => NOK,
            784 => AED,
            512 => OMR,
            654 => SHP,
            586 => PKR,
            590 => PAB,
            598 => PGK,
            600 => PYG,
            604 => PEN,
            985 => PLN,
            643 => RUB,
            646 => RWF,
            946 => RON,
            882 => WST,
            678 => STD,
            682 => SAR,
            748 => SZL,
            690 => SCR,
            891 => CSD,
            702 => SGD,
            760 => SYP,
            703 => SKK,
            705 => SIT,
            90 => SBD,
            706 => SOS,
            736 => SDD,
            968 => SRD,
            840 => USD,
            694 => SLL,
            972 => TJS,
            764 => THB,
            901 => TWD,
            834 => TZS,
            776 => TOP,
            780 => TTD,
            788 => TND,
            795 => TMM,
            949 => TRY,
            800 => UGX,
            860 => UZS,
            980 => UAH,
            858 => UYU,
            242 => FJD,
            608 => PHP,
            238 => FKP,
            953 => XPF,
            191 => HRK,
            203 => CZK,
            152 => CLP,
            752 => SEK,
            144 => LKR,
            232 => ERN,
            233 => EEK,
            230 => ETB,
            //891 => YUM,
            410 => KRW,
            388 => JMD,
            392 => JPY,
            961 => XAG,
            959 => XAU,
            955 => XBA,
            956 => XBB,
            957 => XBC,
            958 => XBD,
            //0 => XFO,
            //0 => XFU,
            964 => XPD,
            962 => XPT,
            963 => XTS,
            999 => XXX,
            _ => default,
        };

        return currency is not null;
    }

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его символьному коду. 
    /// </summary>
    /// <param name="code">Цифровой код валюты.</param>
    /// <returns>Экземпляр <see cref="Currency"/>.</returns>
    /// <exception cref="FormatException" />
    public static Currency Parse(ushort code) => !TryParse(code, out var result) || result is null ? throw new FormatException() : result;

    /// <summary>
    /// Приводит цифровой код валюты к типу <see cref="Currency"/>.
    /// </summary>
    /// <param name="code">Цифровой код валюты.</param>
    /// <returns>Экземпляр <see cref="Currency"/>.</returns>
    /// <exception cref="FormatException" />
    public static Currency FromUInt16(ushort code) => Parse(code);

    /// <summary>
    /// Приводит символьный код валюты к типу <see cref="Currency"/>.
    /// </summary>
    /// <param name="isoCode">Символьный код валюты.</param>
    /// <returns>Экземпляр <see cref="Currency"/>.</returns>
    /// <exception cref="FormatException" />
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
    /// <param name="currency"></param>
    public static implicit operator ushort(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency, nameof(currency));
        return currency.ToUInt16();
    }

    /// <summary>
    /// Неявно преобразует экземпляр <see cref="Currency"/> в символьный код валюты.
    /// </summary>
    /// <param name="currency"></param>
    public static implicit operator string(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency, nameof(currency));
        return currency.ToString();
    }
}