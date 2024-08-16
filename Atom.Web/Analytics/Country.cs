using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Atom.Reactive;

namespace Atom.Web.Analytics;

/// <summary>
/// Данные о стране.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Country"/>.
/// </remarks>
/// <param name="name">Название (на русском).</param>
/// <param name="internationalName">Название (интернациональное).</param>
/// <param name="code">Цифровой код страны.</param>
/// <param name="isoCode2">Двухсимвольный код страны.</param>
/// <param name="isoCode3">Трёхсимвольный код страны.</param>
/// <param name="timeZoneOffset">Смещение часового пояса.</param>
/// <param name="currencies">Валюты страны.</param>
/// <param name="dialCode">Международный телефонный код страны.</param>
/// <param name="domain">Домен.</param>
/// <param name="ioc">Международный олимпийский код.</param>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
[JsonConverter(typeof(CountryJsonConverter))]
[Serializable]
public class Country(string name, string internationalName, ushort code, string isoCode2, string isoCode3, TimeSpan timeZoneOffset, IEnumerable<Currency> currencies, ushort dialCode, string? domain, string? ioc) : Reactively, IParsable<Country?>, IEquatable<Country>, ISerializable
{
    #region Инициализации

    private static readonly Lazy<Country> aus = new(() => new Country("Австралия", "Australia", 036, "AU", "AUS", new TimeSpan(10, 0, 0), Currency.AUD, 61, ".au", "AUS"), true);
    private static readonly Lazy<Country> aut = new(() => new Country("Австрия", "Austria", 040, "AT", "AUT", new TimeSpan(1, 0, 0), 43, ".at", "AUT"), true);
    private static readonly Lazy<Country> aze = new(() => new Country("Азербайджан", "Azerbaijan", 031, "AZ", "AZE", new TimeSpan(4, 0, 0), Currency.AZN, 994, ".az", "AZE"), true);
    private static readonly Lazy<Country> ala = new(() => new Country("Аландские острова", "Åland Islands", 248, "AX", "ALA", new TimeSpan(2, 0, 0), 0, ".ax"), true);
    private static readonly Lazy<Country> alb = new(() => new Country("Албания", "Albania", 008, "AL", "ALB", new TimeSpan(1, 0, 0), Currency.ALL, 355, ".al", "ALB"), true);
    private static readonly Lazy<Country> dza = new(() => new Country("Алжир", "Algeria", 012, "DZ", "DZA", new TimeSpan(1, 0, 0), Currency.DZD, 213, ".dz", "ALG"), true);
    private static readonly Lazy<Country> vir = new(() => new Country("Виргинские Острова (США)", "Virgin Islands", 850, "VI", "VIR", new TimeSpan(-4, 0, 0), 0, ".vi", "ISV"), true);
    private static readonly Lazy<Country> asm = new(() => new Country("Американское Самоа", "American Samoa", 016, "AS", "ASM", new TimeSpan(-11, 0, 0), 0, ".as", "ASA"), true);
    private static readonly Lazy<Country> aia = new(() => new Country("Ангилья", "Anguilla", 660, "AI", "AIA", new TimeSpan(-4, 0, 0), 0, ".ai"), true);
    private static readonly Lazy<Country> ago = new(() => new Country("Ангола", "Angola", 024, "AO", "AGO", new TimeSpan(1, 0, 0), Currency.AOA, 244, ".ao", "ANG"), true);
    private static readonly Lazy<Country> and = new(() => new Country("Андорра", "Andorra", 020, "AD", "AND", new TimeSpan(1, 0, 0), 376, ".ad", "AND"), true);
    private static readonly Lazy<Country> ata = new(() => new Country("Антарктика", "Antarctica", 010, "AQ", "ATA", new TimeSpan(-3, 0, 0), 0, ".aq"), true);
    private static readonly Lazy<Country> atg = new(() => new Country("Антигуа и Барбуда", "Antigua and Barbuda", 028, "AG", "ATG", new TimeSpan(-4, 0, 0), 1268, ".ag", "ANT"), true);
    private static readonly Lazy<Country> arg = new(() => new Country("Аргентина", "Argentina", 032, "AR", "ARG", new TimeSpan(-3, 0, 0), Currency.ARS, 54, ".ar", "ARG"), true);
    private static readonly Lazy<Country> arm = new(() => new Country("Армения", "Armenia", 051, "AM", "ARM", new TimeSpan(4, 0, 0), Currency.AMD, 374, ".am", "ARM"), true);
    private static readonly Lazy<Country> abw = new(() => new Country("Аруба", "Aruba", 533, "AW", "ABW", new TimeSpan(-4, 0, 0), Currency.AWG, 0, ".aw", "ARU"), true);
    private static readonly Lazy<Country> afg = new(() => new Country("Афганистан", "Afghanistan", 004, "AF", "AFG", new TimeSpan(4, 30, 0), Currency.AFN, 93, ".af", "AFG"), true);
    private static readonly Lazy<Country> bhs = new(() => new Country("Багамские Острова", "Bahamas", 044, "BS", "BHS", new TimeSpan(-5, 0, 0), Currency.BSD, 1242, ".bs", "BAH"), true);
    private static readonly Lazy<Country> bgd = new(() => new Country("Бангладеш", "Bangladesh", 050, "BD", "BGD", new TimeSpan(6, 0, 0), Currency.BDT, 880, ".bd", "BAN"), true);
    private static readonly Lazy<Country> brb = new(() => new Country("Барбадос", "Barbados", 052, "BB", "BRB", new TimeSpan(-4, 0, 0), Currency.BBD, 1246, ".bb", "BAR"), true);
    private static readonly Lazy<Country> bhr = new(() => new Country("Бахрейн", "Bahrain", 048, "BH", "BHR", new TimeSpan(3, 0, 0), Currency.BHD, 973, ".bh", "BRN"), true);
    private static readonly Lazy<Country> blz = new(() => new Country("Белиз", "Belize", 084, "BZ", "BLZ", new TimeSpan(-6, 0, 0), Currency.BZD, 501, ".bz", "BIZ"), true);
    private static readonly Lazy<Country> blr = new(() => new Country("Белоруссия", "Belarus", 112, "BY", "BLR", new TimeSpan(3, 0, 0), Currency.BYN, 375, ".by", "BLR"), true);
    private static readonly Lazy<Country> bel = new(() => new Country("Бельгия", "Belgium", 056, "BE", "BEL", new TimeSpan(1, 0, 0), 32, ".be", "BEL"), true);
    private static readonly Lazy<Country> ben = new(() => new Country("Бенин", "Benin", 204, "BJ", "BEN", new TimeSpan(1, 0, 0), 229, ".bj", "BEN"), true);
    private static readonly Lazy<Country> bmu = new(() => new Country("Бермудские Острова", "Bermuda", 060, "BM", "BMU", new TimeSpan(-4, 0, 0), Currency.BMD, 0, ".bm", "BER"), true);
    private static readonly Lazy<Country> bgr = new(() => new Country("Болгария", "Bulgaria", 100, "BG", "BGR", new TimeSpan(2, 0, 0), Currency.BGN, 359, ".bg", "BUL"), true);
    private static readonly Lazy<Country> bol = new(() => new Country("Боливия", "Bolivia", 068, "BO", "BOL", new TimeSpan(-4, 0, 0), [Currency.BOB, Currency.BOV], 591, ".bo", "BOL"), true);
    private static readonly Lazy<Country> bes = new(() => new Country("Бонайре, Синт-Эстатиус и Саба", "Caribbean Netherlands", 535, "BQ", "BES", new TimeSpan(-4, 0, 0), 0, ".bq"), true);
    private static readonly Lazy<Country> bih = new(() => new Country("Босния и Герцеговина", "Bosnia and Herzegovina", 070, "BA", "BIH", new TimeSpan(1, 0, 0), Currency.BAM, 387, ".ba", "BIH"), true);
    private static readonly Lazy<Country> bwa = new(() => new Country("Ботсвана", "Botswana", 072, "BW", "BWA", new TimeSpan(2, 0, 0), Currency.BWP, 267, ".bw", "BOT"), true);
    private static readonly Lazy<Country> bra = new(() => new Country("Бразилия", "Brazil", 076, "BR", "BRA", new TimeSpan(-3, 0, 0), Currency.BRL, 55, ".br", "BRA"), true);
    private static readonly Lazy<Country> iot = new(() => new Country("Британская Территория в Индийском Океане", "British Indian Ocean Territory", 086, "IO", "IOT", new TimeSpan(6, 0, 0), 0, ".io"), true);
    private static readonly Lazy<Country> vgb = new(() => new Country("Виргинские Острова (Великобритания)", "British Virgin Islands", 092, "VG", "VGB", new TimeSpan(-4, 0, 0), 0, ".vg", "IVB"), true);
    private static readonly Lazy<Country> brn = new(() => new Country("Бруней", "Brunei", 096, "BN", "BRN", new TimeSpan(8, 0, 0), Currency.BND, 673, ".bn", "BRU"), true);
    private static readonly Lazy<Country> bfa = new(() => new Country("Буркина-Фасо", "Burkina Faso", 854, "BF", "BFA", new TimeSpan(), 226, ".bf", "BUR"), true);
    private static readonly Lazy<Country> bdi = new(() => new Country("Бурунди", "Burundi", 108, "BI", "BDI", new TimeSpan(2, 0, 0), Currency.BIF, 257, ".bi", "BDI"), true);
    private static readonly Lazy<Country> btn = new(() => new Country("Бутан", "Bhutan", 064, "BT", "BTN", new TimeSpan(6, 0, 0), Currency.BTN, 975, ".bt", "BHU"), true);
    private static readonly Lazy<Country> vut = new(() => new Country("Вануату", "Vanuatu", 548, "VU", "VUT", new TimeSpan(11, 0, 0), Currency.VUV, 678, ".vu", "VAN"), true);
    private static readonly Lazy<Country> vat = new(() => new Country("Ватикан", "Vatican City", 336, "VA", "VAT", new TimeSpan(1, 0, 0), 379, ".va"), true);
    private static readonly Lazy<Country> gbr = new(() => new Country("Великобритания", "United Kingdom", 826, "GB", "GBR", new TimeSpan(), Currency.GBP, 44, ".uk", "GBR"), true);
    private static readonly Lazy<Country> hun = new(() => new Country("Венгрия", "Hungary", 348, "HU", "HUN", new TimeSpan(1, 0, 0), Currency.HUF, 36, ".hu", "HUN"), true);
    private static readonly Lazy<Country> ven = new(() => new Country("Венесуэла", "Venezuela", 862, "VE", "VEN", new TimeSpan(-4, -30, 0), Currency.VEF, 58, ".ve", "VEN"), true);
    private static readonly Lazy<Country> umi = new(() => new Country("Внешние малые острова США", "United States Minor Outlying Islands", 581, "UM", "UMI", new TimeSpan(-11, 0, 0), 0, ".us"), true);
    private static readonly Lazy<Country> tls = new(() => new Country("Восточный Тимор", "East Timor", 626, "TL", "TLS", new TimeSpan(9, 0, 0), 670, ".tl", "TLS"), true);
    private static readonly Lazy<Country> vnm = new(() => new Country("Вьетнам", "Vietnam", 704, "VN", "VNM", new TimeSpan(7, 0, 0), Currency.VND, 84, ".vn", "VIE"), true);
    private static readonly Lazy<Country> gab = new(() => new Country("Габон", "Gabon", 266, "GA", "GAB", new TimeSpan(1, 0, 0), 241, ".ga", "GAB"), true);
    private static readonly Lazy<Country> hti = new(() => new Country("Республика Гаити", "Haiti", 332, "HT", "HTI", new TimeSpan(-5, 0, 0), Currency.HTG, 509, ".ht", "HAI"), true);
    private static readonly Lazy<Country> guy = new(() => new Country("Гайана", "Guyana", 328, "GY", "GUY", new TimeSpan(-4, 0, 0), Currency.GYD, 592, ".gy", "GUY"), true);
    private static readonly Lazy<Country> gmb = new(() => new Country("Гамбия", "Gambia", 270, "GM", "GMB", new TimeSpan(), Currency.GMD, 220, ".gm", "GAM"), true);
    private static readonly Lazy<Country> gha = new(() => new Country("Гана", "Ghana", 288, "GH", "GHA", new TimeSpan(), Currency.GHS, 233, ".gh", "GHA"), true);
    private static readonly Lazy<Country> glp = new(() => new Country("Гваделупа", "Guadeloupe", 312, "GP", "GLP", new TimeSpan(-4, 0, 0), 0, ".gp"), true);
    private static readonly Lazy<Country> gtm = new(() => new Country("Гватемала", "Guatemala", 320, "GT", "GTM", new TimeSpan(-6, 0, 0), Currency.GTQ, 502, ".gt", "GUA"), true);
    private static readonly Lazy<Country> guf = new(() => new Country("Гвиана (департамент Франции)", "French Guiana", 254, "GF", "GUF", new TimeSpan(-3, 0, 0), 0, ".gf"), true);
    private static readonly Lazy<Country> gin = new(() => new Country("Гвинея", "Guinea", 324, "GN", "GIN", new TimeSpan(), Currency.GNF, 224, ".gn", "GUI"), true);
    private static readonly Lazy<Country> gnb = new(() => new Country("Гвинея-Бисау", "Guinea-Bissau", 624, "GW", "GNB", new TimeSpan(), 245, ".gw", "GBS"), true);
    private static readonly Lazy<Country> deu = new(() => new Country("Германия", "Germany", 276, "DE", "DEU", new TimeSpan(1, 0, 0), 49, ".de", "GER"), true);
    private static readonly Lazy<Country> ggy = new(() => new Country("Гернси (коронное владение)", "Guernsey", 831, "GG", "GGY", new TimeSpan(), Currency.GGP, 0, ".gg"), true);
    private static readonly Lazy<Country> gib = new(() => new Country("Гибралтар", "Gibraltar", 292, "GI", "GIB", new TimeSpan(1, 0, 0), Currency.GIP, 0, ".gi"), true);
    private static readonly Lazy<Country> hnd = new(() => new Country("Гондурас", "Honduras", 340, "HN", "HND", new TimeSpan(-6, 0, 0), Currency.HNL, 504, ".hn", "HON"), true);
    private static readonly Lazy<Country> hkg = new(() => new Country("Гонконг", "Hong Kong", 344, "HK", "HKG", new TimeSpan(8, 0, 0), Currency.HKD, 0, ".hk", "HKG"), true);
    private static readonly Lazy<Country> grd = new(() => new Country("Гренада", "Grenada", 308, "GD", "GRD", new TimeSpan(-4, 0, 0), 1473, ".gd", "GRN"), true);
    private static readonly Lazy<Country> grl = new(() => new Country("Гренландия (административная единица)", "Greenland", 304, "GL", "GRL", new TimeSpan(-3, 0, 0), 0, ".gl"), true);
    private static readonly Lazy<Country> grc = new(() => new Country("Греция", "Greece", 300, "GR", "GRC", new TimeSpan(2, 0, 0), 30, ".gr", "GRE"), true);
    private static readonly Lazy<Country> geo = new(() => new Country("Грузия", "Georgia", 268, "GE", "GEO", new TimeSpan(4, 0, 0), Currency.GEL, 995, ".ge", "GEO"), true);
    private static readonly Lazy<Country> gum = new(() => new Country("Гуам", "Guam", 316, "GU", "GUM", new TimeSpan(10, 0, 0), 0, ".gu", "GUM"), true);
    private static readonly Lazy<Country> dnk = new(() => new Country("Дания", "Denmark", 208, "DK", "DNK", new TimeSpan(1, 0, 0), Currency.DKK, 45, ".dk", "DEN"), true);
    private static readonly Lazy<Country> jey = new(() => new Country("Джерси (остров)", "Jersey", 832, "JE", "JEY", new TimeSpan(), Currency.JEP, 0, ".je"), true);
    private static readonly Lazy<Country> dji = new(() => new Country("Джибути", "Djibouti", 262, "DJ", "DJI", new TimeSpan(3, 0, 0), Currency.DJF, 253, ".dj", "DJI"), true);
    private static readonly Lazy<Country> dma = new(() => new Country("Доминика", "Dominica", 212, "DM", "DMA", new TimeSpan(-4, 0, 0), 1767, ".dm", "DMA"), true);
    private static readonly Lazy<Country> dom = new(() => new Country("Доминиканская Республика", "Dominican Republic", 214, "DO", "DOM", new TimeSpan(-4, 0, 0), Currency.DOP, 1809, ".do", "DOM"), true);
    private static readonly Lazy<Country> cod = new(() => new Country("Демократическая Республика Конго", "Democratic Republic of the Congo", 180, "CD", "COD", new TimeSpan(1, 0, 0), Currency.CDF, 243, ".cd", "COD"), true);
    private static readonly Lazy<Country> egy = new(() => new Country("Египет", "Egypt", 818, "EG", "EGY", new TimeSpan(2, 0, 0), Currency.EGP, 20, ".eg", "EGY"), true);
    private static readonly Lazy<Country> zmb = new(() => new Country("Замбия", "Zambia", 894, "ZM", "ZMB", new TimeSpan(2, 0, 0), Currency.ZMW, 260, ".zm", "ZAM"), true);
    private static readonly Lazy<Country> esh = new(() => new Country("Сахарская Арабская Демократическая Республика", "Western Sahara", 732, "EH", "ESH", new TimeSpan(), 0, ".eh"), true);
    private static readonly Lazy<Country> zwe = new(() => new Country("Зимбабве", "Zimbabwe", 716, "ZW", "ZWE", new TimeSpan(2, 0, 0), Currency.ZWL, 263, ".zw", "ZIM"), true);
    private static readonly Lazy<Country> isr = new(() => new Country("Израиль", "Israel", 376, "IL", "ISR", new TimeSpan(2, 0, 0), Currency.ILS, 972, ".il", "ISR"), true);
    private static readonly Lazy<Country> ind = new(() => new Country("Индия", "India", 356, "IN", "IND", new TimeSpan(5, 30, 0), Currency.INR, 91, ".in", "IND"), true);
    private static readonly Lazy<Country> idn = new(() => new Country("Индонезия", "Indonesia", 360, "ID", "IDN", new TimeSpan(7, 0, 0), Currency.IDR, 62, ".id", "INA"), true);
    private static readonly Lazy<Country> jor = new(() => new Country("Иордания", "Jordan", 400, "JO", "JOR", new TimeSpan(2, 0, 0), Currency.JOD, 962, ".jo", "JOR"), true);
    private static readonly Lazy<Country> irq = new(() => new Country("Ирак", "Iraq", 368, "IQ", "IRQ", new TimeSpan(3, 0, 0), Currency.IQD, 964, ".iq", "IRQ"), true);
    private static readonly Lazy<Country> irn = new(() => new Country("Иран", "Iran", 364, "IR", "IRN", new TimeSpan(3, 30, 0), Currency.IRR, 98, ".ir", "IRI"), true);
    private static readonly Lazy<Country> irl = new(() => new Country("Ирландия", "Ireland", 372, "IE", "IRL", new TimeSpan(), 353, ".ie", "IRL"), true);
    private static readonly Lazy<Country> isl = new(() => new Country("Исландия", "Iceland", 352, "IS", "ISL", new TimeSpan(), Currency.ISK, 354, ".is", "ISL"), true);
    private static readonly Lazy<Country> esp = new(() => new Country("Испания", "Spain", 724, "ES", "ESP", new TimeSpan(1, 0, 0), 34, ".es", "ESP"), true);
    private static readonly Lazy<Country> ita = new(() => new Country("Италия", "Italy", 380, "IT", "ITA", new TimeSpan(1, 0, 0), 39, ".it", "ITA"), true);
    private static readonly Lazy<Country> yem = new(() => new Country("Йемен", "Yemen", 887, "YE", "YEM", new TimeSpan(3, 0, 0), Currency.YER, 967, ".ye", "YEM"), true);
    private static readonly Lazy<Country> cpv = new(() => new Country("Кабо-Верде", "Cape Verde", 132, "CV", "CPV", new TimeSpan(-1, 0, 0), Currency.CVE, 238, ".cv", "CPV"), true);
    private static readonly Lazy<Country> kaz = new(() => new Country("Казахстан", "Kazakhstan", 398, "KZ", "KAZ", new TimeSpan(6, 0, 0), Currency.KZT, 7, ".kz", "KAZ"), true);
    private static readonly Lazy<Country> cym = new(() => new Country("Острова Кайман", "Cayman Islands", 136, "KY", "CYM", new TimeSpan(-5, 0, 0), Currency.KYD, 0, ".ky", "CAY"), true);
    private static readonly Lazy<Country> khm = new(() => new Country("Камбоджа", "Cambodia", 116, "KH", "KHM", new TimeSpan(7, 0, 0), Currency.KHR, 855, ".kh", "CAM"), true);
    private static readonly Lazy<Country> cmr = new(() => new Country("Камерун", "Cameroon", 120, "CM", "CMR", new TimeSpan(1, 0, 0), 237, ".cm", "CMR"), true);
    private static readonly Lazy<Country> can = new(() => new Country("Канада", "Canada", 124, "CA", "CAN", new TimeSpan(-5, 0, 0), Currency.CAD, 1, ".ca", "CAN"), true);
    private static readonly Lazy<Country> qat = new(() => new Country("Катар", "Qatar", 634, "QA", "QAT", new TimeSpan(3, 0, 0), Currency.QAR, 974, ".qa", "QAT"), true);
    private static readonly Lazy<Country> ken = new(() => new Country("Кения", "Kenya", 404, "KE", "KEN", new TimeSpan(3, 0, 0), Currency.KES, 254, ".ke", "KEN"), true);
    private static readonly Lazy<Country> cyp = new(() => new Country("Республика Кипр", "Cyprus", 196, "CY", "CYP", new TimeSpan(2, 0, 0), 357, ".cy", "CYP"), true);
    private static readonly Lazy<Country> kgz = new(() => new Country("Кыргызстан", "Kyrgyzstan", 417, "KG", "KGZ", new TimeSpan(6, 0, 0), Currency.KGS, 996, ".kg", "KGZ"), true);
    private static readonly Lazy<Country> kir = new(() => new Country("Кирибати", "Kiribati", 296, "KI", "KIR", new TimeSpan(12, 0, 0), 686, ".ki", "KIR"), true);
    private static readonly Lazy<Country> twn = new(() => new Country("Китайская Республика (Тайвань)", "Taiwan", 158, "TW", "TWN", new TimeSpan(8, 0, 0), Currency.TWD, 0, ".tw", "TPE"), true);
    private static readonly Lazy<Country> prk = new(() => new Country("Корейская Народно-Демократическая Республика", "North Korea", 408, "KP", "PRK", new TimeSpan(9, 0, 0), Currency.KPW, 850, ".kp", "PRK"), true);
    private static readonly Lazy<Country> chn = new(() => new Country("Китай", "China", 156, "CN", "CHN", new TimeSpan(8, 0, 0), Currency.CNY, 86, ".cn", "CHN"), true);
    private static readonly Lazy<Country> cck = new(() => new Country("Кокосовые острова", "Cocos (Keeling) Islands", 166, "CC", "CCK", new TimeSpan(6, 30, 0), 0, ".cc"), true);
    private static readonly Lazy<Country> col = new(() => new Country("Колумбия", "Colombia", 170, "CO", "COL", new TimeSpan(-5, 0, 0), [Currency.COP, Currency.COU], 57, ".co", "COL"), true);
    private static readonly Lazy<Country> com = new(() => new Country("Коморы", "Comoros", 174, "KM", "COM", new TimeSpan(3, 0, 0), Currency.KMF, 269, ".km", "COM"), true);
    private static readonly Lazy<Country> cri = new(() => new Country("Коста-Рика", "Costa Rica", 188, "CR", "CRI", new TimeSpan(-6, 0, 0), Currency.CRC, 506, ".cr", "CRC"), true);
    private static readonly Lazy<Country> civ = new(() => new Country("Кот-д’Ивуар", "Ivory Coast", 384, "CI", "CIV", new TimeSpan(), 225, ".ci", "CIV"), true);
    private static readonly Lazy<Country> cub = new(() => new Country("Куба", "Cuba", 192, "CU", "CUB", new TimeSpan(-5, 0, 0), [Currency.CUC, Currency.CUP], 53, ".cu", "CUB"), true);
    private static readonly Lazy<Country> kwt = new(() => new Country("Кувейт", "Kuwait", 414, "KW", "KWT", new TimeSpan(3, 0, 0), Currency.KWD, 965, ".kw", "KUW"), true);
    private static readonly Lazy<Country> cuw = new(() => new Country("Кюрасао", "Curacao", 531, "CW", "CUW", new TimeSpan(-4, 0, 0), 0, ".cw"), true);
    private static readonly Lazy<Country> lao = new(() => new Country("Лаос", "Laos", 418, "LA", "LAO", new TimeSpan(7, 0, 0), Currency.LAK, 856, ".la", "LAO"), true);
    private static readonly Lazy<Country> lva = new(() => new Country("Латвия", "Latvia", 428, "LV", "LVA", new TimeSpan(2, 0, 0), 371, ".lv", "LAT"), true);
    private static readonly Lazy<Country> lso = new(() => new Country("Лесото", "Lesotho", 426, "LS", "LSO", new TimeSpan(2, 0, 0), Currency.LSL, 266, ".ls", "LES"), true);
    private static readonly Lazy<Country> lbr = new(() => new Country("Либерия", "Liberia", 430, "LR", "LBR", new TimeSpan(), Currency.LRD, 231, ".lr", "LBR"), true);
    private static readonly Lazy<Country> lbn = new(() => new Country("Ливан", "Lebanon", 422, "LB", "LBN", new TimeSpan(2, 0, 0), Currency.LBP, 961, ".lb", "LBN"), true);
    private static readonly Lazy<Country> lby = new(() => new Country("Ливия", "Libya", 434, "LY", "LBY", new TimeSpan(2, 0, 0), Currency.LYD, 218, ".ly", "LBA"), true);
    private static readonly Lazy<Country> ltu = new(() => new Country("Литва", "Lithuania", 440, "LT", "LTU", new TimeSpan(2, 0, 0), Currency.LTL, 370, ".lt", "LTU"), true);
    private static readonly Lazy<Country> lie = new(() => new Country("Лихтенштейн", "Liechtenstein", 438, "LI", "LIE", new TimeSpan(1, 0, 0), 423, ".li", "LIE"), true);
    private static readonly Lazy<Country> lux = new(() => new Country("Люксембург", "Luxembourg", 442, "LU", "LUX", new TimeSpan(1, 0, 0), 352, ".lu", "LUX"), true);
    private static readonly Lazy<Country> mus = new(() => new Country("Маврикий", "Mauritius", 480, "MU", "MUS", new TimeSpan(4, 0, 0), Currency.MUR, 230, ".mu", "MRI"), true);
    private static readonly Lazy<Country> mrt = new(() => new Country("Мавритания", "Mauritania", 478, "MR", "MRT", new TimeSpan(), Currency.MRU, 222, ".mr", "MTN"), true);
    private static readonly Lazy<Country> mdg = new(() => new Country("Мадагаскар", "Madagascar", 450, "MG", "MDG", new TimeSpan(3, 0, 0), Currency.MGA, 261, ".mg", "MAD"), true);
    private static readonly Lazy<Country> myt = new(() => new Country("Майотта", "Mayotte", 175, "YT", "MYT", new TimeSpan(3, 0, 0), 0, ".yt"), true);
    private static readonly Lazy<Country> mac = new(() => new Country("Макао", "Macao", 446, "MO", "MAC", new TimeSpan(8, 0, 0), Currency.MOP, 0, ".mo"), true);
    private static readonly Lazy<Country> mkd = new(() => new Country("Северная Македония", "North Macedonia", 807, "MK", "MKD", new TimeSpan(1, 0, 0), Currency.MKD, 389, ".mk", "MKD"), true);
    private static readonly Lazy<Country> mwi = new(() => new Country("Малави", "Malawi", 454, "MW", "MWI", new TimeSpan(2, 0, 0), Currency.MWK, 265, ".mw", "MAW"), true);
    private static readonly Lazy<Country> mys = new(() => new Country("Малайзия", "Malaysia", 458, "MY", "MYS", new TimeSpan(8, 0, 0), Currency.MYR, 60, ".my", "MAS"), true);
    private static readonly Lazy<Country> mli = new(() => new Country("Мали", "Mali", 466, "ML", "MLI", new TimeSpan(), 223, ".ml", "MLI"), true);
    private static readonly Lazy<Country> mdv = new(() => new Country("Мальдивы", "Maldives", 462, "MV", "MDV", new TimeSpan(5, 0, 0), Currency.MVR, 960, ".mv", "MDV"), true);
    private static readonly Lazy<Country> mlt = new(() => new Country("Мальта", "Malta", 470, "MT", "MLT", new TimeSpan(1, 0, 0), 356, ".mt", "MLT"), true);
    private static readonly Lazy<Country> mar = new(() => new Country("Марокко", "Morocco", 504, "MA", "MAR", new TimeSpan(), Currency.MAD, 212, ".ma", "MAR"), true);
    private static readonly Lazy<Country> mtq = new(() => new Country("Мартиника", "Martinique", 474, "MQ", "MTQ", new TimeSpan(-4, 0, 0), 0, ".mq"), true);
    private static readonly Lazy<Country> mhl = new(() => new Country("Маршалловы Острова", "Marshall Islands", 584, "MH", "MHL", new TimeSpan(12, 0, 0), 692, ".mh", "MHL"), true);
    private static readonly Lazy<Country> mex = new(() => new Country("Мексика", "Mexico", 484, "MX", "MEX", new TimeSpan(-6, 0, 0), [Currency.MXN, Currency.MXV], 52, ".mx", "MEX"), true);
    private static readonly Lazy<Country> fsm = new(() => new Country("Федеративные Штаты Микронезии", "Federated States of Micronesia", 583, "FM", "FSM", new TimeSpan(10 , 0, 0), 691, ".fm", "FSM"), true);
    private static readonly Lazy<Country> moz = new(() => new Country("Мозамбик", "Mozambique", 508, "MZ", "MOZ", new TimeSpan(2, 0, 0), Currency.MZN, 258, ".mz", "MOZ"), true);
    private static readonly Lazy<Country> mda = new(() => new Country("Молдавия", "Moldova", 498, "MD", "MDA", new TimeSpan(2, 0, 0), Currency.MDL, 373, ".md", "MDA"), true);
    private static readonly Lazy<Country> mco = new(() => new Country("Монако", "Principality of Monaco", 492, "MC", "MCO", new TimeSpan(1, 0, 0), 377, ".mc", "MON"), true);
    private static readonly Lazy<Country> mng = new(() => new Country("Монголия", "Mongolia", 496, "MN", "MNG", new TimeSpan(8, 0, 0), Currency.MNT, 976, ".mn", "MGL"), true);
    private static readonly Lazy<Country> msr = new(() => new Country("Монтсеррат", "Montserrat", 500, "MS", "MSR", new TimeSpan(-4, 0, 0), 0, ".ms"), true);
    private static readonly Lazy<Country> mmr = new(() => new Country("Мьянма", "Myanmar", 104, "MM", "MMR", new TimeSpan(6, 30, 0), Currency.MMK, 95, ".mm", "MYA"), true);
    private static readonly Lazy<Country> nam = new(() => new Country("Намибия", "Namibia", 516, "NA", "NAM", new TimeSpan(1, 0, 0), Currency.NAD, 264, ".na", "NAM"), true);
    private static readonly Lazy<Country> nru = new(() => new Country("Науру", "Nauru", 520, "NR", "NRU", new TimeSpan(12, 0, 0), 674, ".nr", "NRU"), true);
    private static readonly Lazy<Country> npl = new(() => new Country("Непал", "Nepal", 524, "NP", "NPL", new TimeSpan(5, 45, 0), Currency.NPR, 977, ".np", "NEP"), true);
    private static readonly Lazy<Country> ner = new(() => new Country("Нигер", "Niger", 562, "NE", "NER", new TimeSpan(1, 0, 0), 227, ".ne", "NIG"), true);
    private static readonly Lazy<Country> nga = new(() => new Country("Нигерия", "Nigeria", 566, "NG", "NGA", new TimeSpan(1, 0, 0), Currency.NGN, 234, ".ng", "NGR"), true);
    private static readonly Lazy<Country> nld = new(() => new Country("Нидерланды", "Netherlands", 528, "NL", "NLD", new TimeSpan(1, 0, 0), 31, ".nl", "NED"), true);
    private static readonly Lazy<Country> nic = new(() => new Country("Никарагуа", "Nicaragua", 558, "NI", "NIC", new TimeSpan(-6, 0, 0), Currency.NIO, 505, ".ni", "NCA"), true);
    private static readonly Lazy<Country> niu = new(() => new Country("Ниуэ", "Niue", 570, "NU", "NIU", new TimeSpan(-11, 0, 0), 0, ".nu"), true);
    private static readonly Lazy<Country> nzl = new(() => new Country("Новая Зеландия", "New Zealand", 554, "NZ", "NZL", new TimeSpan(12, 0, 0), Currency.NZD, 64, ".nz", "NZL"), true);
    private static readonly Lazy<Country> ncl = new(() => new Country("Новая Каледония", "New Caledonia", 540, "NC", "NCL", new TimeSpan(11, 0, 0), 0, ".nc"), true);
    private static readonly Lazy<Country> nor = new(() => new Country("Норвегия", "Norway", 578, "NO", "NOR", new TimeSpan(1, 0, 0), Currency.NOK, 47, ".no", "NOR"), true);
    private static readonly Lazy<Country> are = new(() => new Country("Объединённые Арабские Эмираты", "United Arab Emirates", 784, "AE", "ARE", new TimeSpan(4, 0, 0), Currency.AED, 971, ".ae", "UAE"), true);
    private static readonly Lazy<Country> omn = new(() => new Country("Оман", "Oman", 512, "OM", "OMN", new TimeSpan(4, 0, 0), Currency.OMR, 968, ".om", "OMA"), true);
    private static readonly Lazy<Country> bvt = new(() => new Country("Остров Буве", "Bouvet Island", 074, "BV", "BVT", new TimeSpan(), 0, ".bv"), true);
    private static readonly Lazy<Country> imn = new(() => new Country("Остров Мэн", "Isle of Man", 833, "IM", "IMN", new TimeSpan(), Currency.IMP, 0, ".im"), true);
    private static readonly Lazy<Country> cok = new(() => new Country("Острова Кука", "Cook Islands", 184, "CK", "COK", new TimeSpan(-10, 0, 0), 0, ".ck", "COK"), true);
    private static readonly Lazy<Country> nfk = new(() => new Country("Остров Норфолк", "Norfolk Island", 574, "NF", "NFK", new TimeSpan(11, 0, 0), 0, ".nf"), true);
    private static readonly Lazy<Country> cxr = new(() => new Country("Остров Рождества (Австралия)", "Christmas Island", 162, "CX", "CXR", new TimeSpan(7, 0, 0), 0, ".cx"), true);
    private static readonly Lazy<Country> pcn = new(() => new Country("Острова Питкэрн", "Pitcairn Islands", 612, "PN", "PCN", new TimeSpan(-8, 0, 0), 0, ".pn"), true);
    private static readonly Lazy<Country> shn = new(() => new Country("Остров Святой Елены", "Saint Helena, Ascension and Tristan da Cunha", 654, "SH", "SHN", new TimeSpan(), Currency.SHP, 0, ".sh"), true);
    private static readonly Lazy<Country> pak = new(() => new Country("Пакистан", "Pakistan", 586, "PK", "PAK", new TimeSpan(5, 0, 0), Currency.PKR, 92, ".pk", "PAK"), true);
    private static readonly Lazy<Country> plw = new(() => new Country("Палау", "Palau", 585, "PW", "PLW", new TimeSpan(9, 0, 0), 680, ".pw", "PLW"), true);
    private static readonly Lazy<Country> pse = new(() => new Country("Государство Палестина", "Palestine", 275, "PS", "PSE", new TimeSpan(2, 0, 0), 970, ".ps", "PLE"), true);
    private static readonly Lazy<Country> pan = new(() => new Country("Панама", "Panama", 591, "PA", "PAN", new TimeSpan(-5, 0, 0), Currency.PAB, 507, ".pa", "PAN"), true);
    private static readonly Lazy<Country> png = new(() => new Country("Папуа — Новая Гвинея", "Papua New Guinea", 598, "PG", "PNG", new TimeSpan(10, 0, 0), Currency.PGK, 675, ".pg", "PNG"), true);
    private static readonly Lazy<Country> pry = new(() => new Country("Парагвай", "Paraguay", 600, "PY", "PRY", new TimeSpan(-4, 0, 0), Currency.PYG, 595, ".py", "PAR"), true);
    private static readonly Lazy<Country> per = new(() => new Country("Перу", "Peru", 604, "PE", "PER", new TimeSpan(-5, 0, 0), Currency.PEN, 51, ".pe", "PER"), true);
    private static readonly Lazy<Country> pol = new(() => new Country("Польша", "Poland", 616, "PL", "POL", new TimeSpan(1, 0, 0), Currency.PLN, 48, ".pl", "POL"), true);
    private static readonly Lazy<Country> prt = new(() => new Country("Португалия", "Portugal", 620, "PT", "PRT", new TimeSpan(), 351, ".pt", "POR"), true);
    private static readonly Lazy<Country> pri = new(() => new Country("Пуэрто-Рико", "Puerto Rico", 630, "PR", "PRI", new TimeSpan(-4, 0, 0), 0, ".pr", "PUR"), true);
    private static readonly Lazy<Country> cog = new(() => new Country("Республика Конго", "Republic of the Congo", 178, "CG", "COG", new TimeSpan(1, 0, 0), 242, ".cg", "CGO"), true);
    private static readonly Lazy<Country> kor = new(() => new Country("Республика Корея", "South Korea", 410, "KR", "KOR", new TimeSpan(9, 0, 0), Currency.KRW, 82, ".kr", "KOR"), true);
    private static readonly Lazy<Country> reu = new(() => new Country("Реюньон", "Reunion", 638, "RE", "REU", new TimeSpan(4, 0, 0), 0, ".re"), true);
    private static readonly Lazy<Country> rus = new(() => new Country("Россия", "Russia", 643, "RU", "RUS", new TimeSpan(3, 0, 0), Currency.RUB, 7, ".ru", "RUS"), true);
    private static readonly Lazy<Country> rwa = new(() => new Country("Руанда", "Rwanda", 646, "RW", "RWA", new TimeSpan(2, 0, 0), Currency.RWF, 250, ".rw", "RWA"), true);
    private static readonly Lazy<Country> rou = new(() => new Country("Румыния", "Romania", 642, "RO", "ROU", new TimeSpan(2, 0, 0), Currency.RON, 40, ".ro", "ROU"), true);
    private static readonly Lazy<Country> slv = new(() => new Country("Сальвадор", "El Salvador", 222, "SV", "SLV", new TimeSpan(-6, 0, 0), Currency.SVC, 503, ".sv", "ESA"), true);
    private static readonly Lazy<Country> wsm = new(() => new Country("Самоа", "Samoa", 882, "WS", "WSM", new TimeSpan(13, 0, 0), Currency.WST, 685, ".ws", "SAM"), true);
    private static readonly Lazy<Country> smr = new(() => new Country("Сан-Марино", "San Marino", 674, "SM", "SMR", new TimeSpan(1, 0, 0), 378, ".sm", "SMR"), true);
    private static readonly Lazy<Country> stp = new(() => new Country("Сан-Томе и Принсипи", "Sao Tome and Principe", 678, "ST", "STP", new TimeSpan(), Currency.STN, 239, ".st", "STP"), true);
    private static readonly Lazy<Country> sau = new(() => new Country("Саудовская Аравия", "Saudi Arabia", 682, "SA", "SAU", new TimeSpan(3, 0, 0), Currency.SAR, 966, ".sa", "KSA"), true);
    private static readonly Lazy<Country> swz = new(() => new Country("Эсватини", "Eswatini", 748, "SZ", "SWZ", new TimeSpan(2, 0, 0), Currency.SZL, 268, ".sz", "SWZ"), true);
    private static readonly Lazy<Country> mnp = new(() => new Country("Северные Марианские Острова", "Northern Mariana Islands", 580, "MP", "MNP", new TimeSpan(10, 0, 0), 0, ".mp"), true);
    private static readonly Lazy<Country> syc = new(() => new Country("Сейшельские Острова", "Seychelles", 690, "SC", "SYC", new TimeSpan(4, 0, 0), Currency.SCR, 248, ".sc", "SEY"), true);
    private static readonly Lazy<Country> blm = new(() => new Country("Сен-Бартелеми (заморское сообщество)", "Saint Barthelemy", 652, "BL", "BLM", new TimeSpan(-4, 0, 0), 0, ".bl"), true);
    private static readonly Lazy<Country> maf = new(() => new Country("Сен-Мартен (владение Франции)", "Saint Martin", 663, "MF", "MAF", new TimeSpan(-4, 0, 0), 0, ".mf"), true);
    private static readonly Lazy<Country> spm = new(() => new Country("Сен-Пьер и Микелон", "Saint Pierre and Miquelon", 666, "PM", "SPM", new TimeSpan(-3, 0, 0), 0, ".pm"), true);
    private static readonly Lazy<Country> sen = new(() => new Country("Сенегал", "Senegal", 686, "SN", "SEN", new TimeSpan(), 221, ".sn", "SEN"), true);
    private static readonly Lazy<Country> vct = new(() => new Country("Сент-Винсент и Гренадины", "Saint Vincent and the Grenadines", 670, "VC", "VCT", new TimeSpan(-4, 0, 0), 1784, ".vc", "VIN"), true);
    private static readonly Lazy<Country> kna = new(() => new Country("Сент-Китс и Невис", "Saint Kitts and Nevis", 659, "KN", "KNA", new TimeSpan(-4, 0, 0), 1869, ".kn", "SKN"), true);
    private static readonly Lazy<Country> lca = new(() => new Country("Сент-Люсия", "Saint Lucia", 662, "LC", "LCA", new TimeSpan(-4, 0, 0), 1758, ".lc", "LCA"), true);
    private static readonly Lazy<Country> srb = new(() => new Country("Сербия", "Serbia", 688, "RS", "SRB", new TimeSpan(1, 0, 0), Currency.RSD, 381, ".rs", "SRB"), true);
    private static readonly Lazy<Country> sgp = new(() => new Country("Сингапур", "Singapore", 702, "SG", "SGP", new TimeSpan(8, 0, 0), Currency.SGD, 65, ".sg", "SGP"), true);
    private static readonly Lazy<Country> sxm = new(() => new Country("Синт-Мартен", "Sint Maarten", 534, "SX", "SXM", new TimeSpan(-4, 0, 0), 0, ".sx"), true);
    private static readonly Lazy<Country> syr = new(() => new Country("Сирия", "Syria", 760, "SY", "SYR", new TimeSpan(2, 0, 0), Currency.SYP, 963, ".sy", "SYR"), true);
    private static readonly Lazy<Country> svk = new(() => new Country("Словакия", "Slovakia", 703, "SK", "SVK", new TimeSpan(1, 0, 0), 421, ".sk", "SVK"), true);
    private static readonly Lazy<Country> svn = new(() => new Country("Словения", "Slovenia", 705, "SI", "SVN", new TimeSpan(1, 0, 0), 386, ".si", "SLO"), true);
    private static readonly Lazy<Country> slb = new(() => new Country("Соломоновы Острова", "Solomon Islands", 090, "SB", "SLB", new TimeSpan(11, 0, 0), Currency.SBD, 677, ".sb", "SOL"), true);
    private static readonly Lazy<Country> som = new(() => new Country("Сомали", "Somalia", 706, "SO", "SOM", new TimeSpan(3, 0, 0), Currency.SOS, 252, ".so", "SOM"), true);
    private static readonly Lazy<Country> sdn = new(() => new Country("Судан", "Sudan", 729, "SD", "SDN", new TimeSpan(3, 0, 0), Currency.SDG, 249, ".sd", "SUD"), true);
    private static readonly Lazy<Country> sur = new(() => new Country("Суринам", "Suriname", 740, "SR", "SUR", new TimeSpan(-3, 0, 0), Currency.SRD, 597, ".sr", "SUR"), true);
    private static readonly Lazy<Country> usa = new(() => new Country("Соединённые Штаты Америки", "United States of America", 840, "US", "USA", new TimeSpan(-8, 0, 0), [Currency.USD, Currency.USN, Currency.USS], 1, ".us", "USA"), true);
    private static readonly Lazy<Country> sle = new(() => new Country("Сьерра-Леоне", "Sierra Leone", 694, "SL", "SLE", new TimeSpan(), Currency.SLL, 232, ".sl", "SLE"), true);
    private static readonly Lazy<Country> tjk = new(() => new Country("Таджикистан", "Tajikistan", 762, "TJ", "TJK", new TimeSpan(5, 0, 0), Currency.TJS, 992, ".tj", "TJK"), true);
    private static readonly Lazy<Country> tha = new(() => new Country("Таиланд", "Thailand", 764, "TH", "THA", new TimeSpan(7, 0, 0), Currency.THB, 66, ".th", "THA"), true);
    private static readonly Lazy<Country> tza = new(() => new Country("Танзания", "Tanzania", 834, "TZ", "TZA", new TimeSpan(3, 0, 0), Currency.TZS, 255, ".tz", "TAN"), true);
    private static readonly Lazy<Country> tca = new(() => new Country("Теркс и Кайкос", "Turks and Caicos Islands", 796, "TC", "TCA", new TimeSpan(-5, 0, 0), 0, ".tc"), true);
    private static readonly Lazy<Country> tgo = new(() => new Country("Того", "Togo", 768, "TG", "TGO", new TimeSpan(), 228, ".tg", "TOG"), true);
    private static readonly Lazy<Country> tkl = new(() => new Country("Токелау", "Tokelau", 772, "TK", "TKL", new TimeSpan(13, 0, 0), 0, ".tk"), true);
    private static readonly Lazy<Country> ton = new(() => new Country("Тонга", "Tonga", 776, "TO", "TON", new TimeSpan(13, 0, 0), Currency.TOP, 676, ".to", "TGA"), true);
    private static readonly Lazy<Country> tto = new(() => new Country("Тринидад и Тобаго", "Trinidad and Tobago", 780, "TT", "TTO", new TimeSpan(-4, 0, 0), Currency.TTD, 1868, ".tt", "TTO"), true);
    private static readonly Lazy<Country> tuv = new(() => new Country("Тувалу", "Tuvalu", 798, "TV", "TUV", new TimeSpan(12, 0, 0), 688, ".tv", "TUV"), true);
    private static readonly Lazy<Country> tun = new(() => new Country("Тунис", "Tunisia", 788, "TN", "TUN", new TimeSpan(1, 0, 0), Currency.TND, 216, ".tn", "TUN"), true);
    private static readonly Lazy<Country> tkm = new(() => new Country("Туркменистан", "Turkmenistan", 795, "TM", "TKM", new TimeSpan(5, 0, 0), Currency.TMT, 993, ".tm", "TKM"), true);
    private static readonly Lazy<Country> tur = new(() => new Country("Турция", "Turkey", 792, "TR", "TUR", new TimeSpan(2, 0, 0), Currency.TRY, 90, ".tr", "TUR"), true);
    private static readonly Lazy<Country> uga = new(() => new Country("Уганда", "Uganda", 800, "UG", "UGA", new TimeSpan(3, 0, 0), Currency.UGX, 256, ".ug", "UGA"), true);
    private static readonly Lazy<Country> uzb = new(() => new Country("Узбекистан", "Uzbekistan", 860, "UZ", "UZB", new TimeSpan(5, 0, 0), Currency.UZS, 998, ".uz", "UZB"), true);
    private static readonly Lazy<Country> ukr = new(() => new Country("Украина", "Ukraine", 804, "UA", "UKR", new TimeSpan(2, 0, 0), Currency.UAH, 380, ".ua", "UKR"), true);
    private static readonly Lazy<Country> wlf = new(() => new Country("Уоллис и Футуна", "Wallis and Futuna", 876, "WF", "WLF", new TimeSpan(12, 0, 0), 0, ".wf"), true);
    private static readonly Lazy<Country> ury = new(() => new Country("Уругвай", "Uruguay", 858, "UY", "URY", new TimeSpan(-3, 0, 0), [Currency.UYI, Currency.UYU], 598, ".uy", "URU"), true);
    private static readonly Lazy<Country> fro = new(() => new Country("Фарерские острова", "Faroe Islands", 234, "FO", "FRO", new TimeSpan(), 0, ".fo"), true);
    private static readonly Lazy<Country> fji = new(() => new Country("Фиджи", "Fiji", 242, "FJ", "FJI", new TimeSpan(12, 0, 0), Currency.FJD, 679, ".fj", "FIJ"), true);
    private static readonly Lazy<Country> phl = new(() => new Country("Филиппины", "Philippines", 608, "PH", "PHL", new TimeSpan(8, 0, 0), Currency.PHP, 63, ".ph", "PHI"), true);
    private static readonly Lazy<Country> fin = new(() => new Country("Финляндия", "Finland", 246, "FI", "FIN", new TimeSpan(2, 0, 0), 358, ".fi", "FIN"), true);
    private static readonly Lazy<Country> flk = new(() => new Country("Фолклендские острова", "Falkland Islands", 238, "FK", "FLK", new TimeSpan(-3, 0, 0), Currency.FKP, 0, ".fk"), true);
    private static readonly Lazy<Country> fra = new(() => new Country("Франция", "France", 250, "FR", "FRA", new TimeSpan(1, 0, 0), 33, ".fr", "FRA"), true);
    private static readonly Lazy<Country> pyf = new(() => new Country("Французская Полинезия", "French Polynesia", 258, "PF", "PYF", new TimeSpan(-10, 0, 0), 0, ".pf"), true);
    private static readonly Lazy<Country> atf = new(() => new Country("Французские Южные и Антарктические территории", "French Southern and Antarctic Lands", 260, "TF", "ATF", new TimeSpan(5, 0, 0), 0, ".tf"), true);
    private static readonly Lazy<Country> hmd = new(() => new Country("Остров Херд и острова Макдональд", "Heard Island and McDonald Islands", 334, "HM", "HMD", new TimeSpan(4, 0, 0), 0, ".hm"), true);
    private static readonly Lazy<Country> hrv = new(() => new Country("Хорватия", "Croatia", 191, "HR", "HRV", new TimeSpan(1, 0, 0), Currency.HRK, 385, ".hr", "CRO"), true);
    private static readonly Lazy<Country> caf = new(() => new Country("Центральноафриканская Республика", "Central African Republic", 140, "CF", "CAF", new TimeSpan(1, 0, 0), 236, ".cf", "CAF"), true);
    private static readonly Lazy<Country> tcd = new(() => new Country("Чад", "Chad", 148, "TD", "TCD", new TimeSpan(1, 0, 0), 235, ".td", "CHA"), true);
    private static readonly Lazy<Country> mne = new(() => new Country("Черногория", "Montenegro", 499, "ME", "MNE", new TimeSpan(1, 0, 0), 382, ".me", "MNE"), true);
    private static readonly Lazy<Country> cze = new(() => new Country("Чехия", "Czechia", 203, "CZ", "CZE", new TimeSpan(1, 0, 0), Currency.CZK, 420, ".cz", "CZE"), true);
    private static readonly Lazy<Country> chl = new(() => new Country("Чили", "Chile", 152, "CL", "CHL", new TimeSpan(-3, 0, 0), [Currency.CLF, Currency.CLP], 56, ".cl", "CHI"), true);
    private static readonly Lazy<Country> che = new(() => new Country("Швейцария", "Switzerland", 756, "CH", "CHE", new TimeSpan(1, 0, 0), [Currency.CHE, Currency.CHF, Currency.CHW], 41, ".ch", "SUI"), true);
    private static readonly Lazy<Country> swe = new(() => new Country("Швеция", "Sweden", 752, "SE", "SWE", new TimeSpan(1, 0, 0), Currency.SEK, 46, ".se", "SWE"), true);
    private static readonly Lazy<Country> sjm = new(() => new Country("Флаг Шпицбергена и Ян-Майена", "Svalbard", 744, "SJ", "SJM", new TimeSpan(1, 0, 0), 0, ".sj"), true);
    private static readonly Lazy<Country> lka = new(() => new Country("Шри-Ланка", "Sri Lanka", 144, "LK", "LKA", new TimeSpan(5, 30, 0), Currency.LKR, 94, ".lk", "SRI"), true);
    private static readonly Lazy<Country> ecu = new(() => new Country("Эквадор", "Ecuador", 218, "EC", "ECU", new TimeSpan(-5, 0, 0), 593, ".ec", "ECU"), true);
    private static readonly Lazy<Country> gnq = new(() => new Country("Экваториальная Гвинея", "Equatorial Guinea", 226, "GQ", "GNQ", new TimeSpan(1, 0, 0), 240, ".gq", "GEQ"), true);
    private static readonly Lazy<Country> eri = new(() => new Country("Эритрея", "Eritrea", 232, "ER", "ERI", new TimeSpan(3, 0, 0), Currency.ERN, 291, ".er", "ERI"), true);
    private static readonly Lazy<Country> est = new(() => new Country("Эстония", "Estonia", 233, "EE", "EST", new TimeSpan(2, 0, 0), 372, ".ee", "EST"), true);
    private static readonly Lazy<Country> eth = new(() => new Country("Эфиопия", "Ethiopia", 231, "ET", "ETH", new TimeSpan(3, 0, 0), Currency.ETB, 251, ".et", "ETH"), true);
    private static readonly Lazy<Country> zaf = new(() => new Country("Южно-Африканская Республика", "South Africa", 710, "ZA", "ZAF", new TimeSpan(2, 0, 0), Currency.ZAR, 27, ".za", "RSA"), true);
    private static readonly Lazy<Country> sgs = new(() => new Country("Южная Георгия и Южные Сандвичевы Острова", "South Georgia and South Sandwich Islands", 239, "GS", "SGS", new TimeSpan(-2, 0, 0), 0, ".gs"), true);
    private static readonly Lazy<Country> ssd = new(() => new Country("Южный Судан", "South Sudan", 728, "SS", "SSD", new TimeSpan(3, 0, 0), Currency.SSP, 211, ".ss"), true);
    private static readonly Lazy<Country> jam = new(() => new Country("Ямайка", "Jamaica", 388, "JM", "JAM", new TimeSpan(-5, 0, 0), Currency.JMD, 1876, ".jm", "JAM"), true);
    private static readonly Lazy<Country> jpn = new(() => new Country("Япония", "Japan", 392, "JP", "JPN", new TimeSpan(9, 0, 0), Currency.JPY, 81, ".jp", "JPN"), true);

    #endregion
    
    /// <summary>
    /// Название.
    /// </summary>
    public string Name { get; set; } = name;

    /// <summary>
    /// Название (интернациональное).
    /// </summary>
    public string InternationalName { get; set; } = internationalName;

    /// <summary>
    /// Цифровой код страны.
    /// </summary>
    public ushort Code
    {
        get => code;
        set => SetProperty(ref code, value);
    }

    /// <summary>
    /// Двухсимвольный код страны.
    /// </summary>
    public string IsoCode2
    {
        get => isoCode2;
        set => SetProperty(ref isoCode2, value);
    }

    /// <summary>
    /// Трёхсимвольный код страны.
    /// </summary>
    public string IsoCode
    {
        get => isoCode3;
        set => SetProperty(ref isoCode3, value);
    }

    /// <summary>
    /// Валюта страны.
    /// </summary>
    public IEnumerable<Currency> Currencies
    {
        get => currencies;
        set => SetProperty(ref currencies, value);
    }

    /// <summary>
    /// Основная валюта страны.
    /// </summary>
    public Currency? Currency => Currencies.FirstOrDefault();

    /// <summary>
    /// Международный телефонный код страны.
    /// </summary>
    public ushort DialCode
    {
        get => dialCode;
        set => SetProperty(ref dialCode, value);
    }

    /// <summary>
    /// Домен.
    /// </summary>
    public string? Domain
    {
        get => domain;
        set => SetProperty(ref domain, value);
    }

    /// <summary>
    /// Международный олимпийский код.
    /// </summary>
    public string? IOC
    {
        get => ioc;
        set => SetProperty(ref ioc, value);
    }

    /// <summary>
    /// Информация о часовом поясе.
    /// </summary>
    public TimeZoneInfo? TimeZone => TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(t => t.BaseUtcOffset == TimeZoneOffset);

    /// <summary>
    /// Смещение часового пояса от UTC.
    /// </summary>
    public TimeSpan TimeZoneOffset
    {
        get => timeZoneOffset;
        set => SetProperty(ref timeZoneOffset, value);
    }

    /// <summary>
    /// Происходит в момент изменения двухсимвольного кода страны.
    /// </summary>
    public event AsyncEventHandler<Country>? CodeChanged;

    /// <summary>
    /// Происходит в момент изменения трехсимвольного кода страны.
    /// </summary>
    public event AsyncEventHandler<Country>? IsoCodeChanged;

    /// <summary>
    /// Происходит в момент изменения трехсимвольного кода страны.
    /// </summary>
    public event AsyncEventHandler<Country>? IsoCode2Changed;

    #region Коды

    /// <summary>
    /// Австралия.
    /// </summary>
    public static Country AUS => aus.Value;

    /// <summary>
    /// Австрия.
    /// </summary>
    public static Country AUT => aut.Value;

    /// <summary>
    /// Азербайджан.
    /// </summary>
    public static Country AZE => aze.Value;

    /// <summary>
    /// Аландские острова.
    /// </summary>
    public static Country ALA => ala.Value;

    /// <summary>
    /// Албания.
    /// </summary>
    public static Country ALB => alb.Value;

    /// <summary>
    /// Алжир.
    /// </summary>
    public static Country DZA => dza.Value;

    /// <summary>
    /// Виргинские Острова (США).
    /// </summary>
    public static Country VIR => vir.Value;

    /// <summary>
    /// Американское Самоа.
    /// </summary>
    public static Country ASM => asm.Value;

    /// <summary>
    /// Ангилья.
    /// </summary>
    public static Country AIA => aia.Value;

    /// <summary>
    /// Ангола.
    /// </summary>
    public static Country AGO => ago.Value;

    /// <summary>
    /// Андорра.
    /// </summary>
    public static Country AND => and.Value;

    /// <summary>
    /// Антарктика.
    /// </summary>
    public static Country ATA => ata.Value;

    /// <summary>
    /// Антигуа и Барбуда.
    /// </summary>
    public static Country ATG => atg.Value;

    /// <summary>
    /// Аргентина.
    /// </summary>
    public static Country ARG => arg.Value;

    /// <summary>
    /// Армения.
    /// </summary>
    public static Country ARM => arm.Value;

    /// <summary>
    /// Аруба.
    /// </summary>
    public static Country ABW => abw.Value;

    /// <summary>
    /// Афганистан.
    /// </summary>
    public static Country AFG => afg.Value;

    /// <summary>
    /// Багамские Острова.
    /// </summary>
    public static Country BHS => bhs.Value;

    /// <summary>
    /// Бангладеш.
    /// </summary>
    public static Country BGD => bgd.Value;

    /// <summary>
    /// Барбадос.
    /// </summary>
    public static Country BRB => brb.Value;

    /// <summary>
    /// Бахрейн.
    /// </summary>
    public static Country BHR => bhr.Value;

    /// <summary>
    /// Белиз.
    /// </summary>
    public static Country BLZ => blz.Value;

    /// <summary>
    /// Белоруссия.
    /// </summary>
    public static Country BLR => blr.Value;

    /// <summary>
    /// Бельгия.
    /// </summary>
    public static Country BEL => bel.Value;

    /// <summary>
    /// Бенин.
    /// </summary>
    public static Country BEN => ben.Value;

    /// <summary>
    /// Бермудские Острова.
    /// </summary>
    public static Country BMU => bmu.Value;

    /// <summary>
    /// Болгария.
    /// </summary>
    public static Country BGR => bgr.Value;

    /// <summary>
    /// Боливия.
    /// </summary>
    public static Country BOL => bol.Value;

    /// <summary>
    /// Бонайре, Синт-Эстатиус и Саба.
    /// </summary>
    public static Country BES => bes.Value;

    /// <summary>
    /// Босния и Герцеговина.
    /// </summary>
    public static Country BIH => bih.Value;

    /// <summary>
    /// Ботсвана.
    /// </summary>
    public static Country BWA => bwa.Value;

    /// <summary>
    /// Бразилия.
    /// </summary>
    public static Country BRA => bra.Value;

    /// <summary>
    /// Британская Территория в Индийском Океане.
    /// </summary>
    public static Country IOT => iot.Value;

    /// <summary>
    /// Виргинские Острова (Великобритания).
    /// </summary>
    public static Country VGB => vgb.Value;

    /// <summary>
    /// Бруней.
    /// </summary>
    public static Country BRN => brn.Value;

    /// <summary>
    /// Буркина-Фасо.
    /// </summary>
    public static Country BFA => bfa.Value;

    /// <summary>
    /// Бурунди.
    /// </summary>
    public static Country BDI => bdi.Value;

    /// <summary>
    /// Бутан.
    /// </summary>
    public static Country BTN => btn.Value;

    /// <summary>
    /// Вануату.
    /// </summary>
    public static Country VUT => vut.Value;

    /// <summary>
    /// Ватикан.
    /// </summary>
    public static Country VAT => vat.Value;

    /// <summary>
    /// Великобритания.
    /// </summary>
    public static Country GBR => gbr.Value;

    /// <summary>
    /// Венгрия.
    /// </summary>
    public static Country HUN => hun.Value;

    /// <summary>
    /// Венесуэла.
    /// </summary>
    public static Country VEN => ven.Value;

    /// <summary>
    /// Внешние малые острова США.
    /// </summary>
    public static Country UMI => umi.Value;

    /// <summary>
    /// Восточный Тимор.
    /// </summary>
    public static Country TLS => tls.Value;

    /// <summary>
    /// Вьетнам.
    /// </summary>
    public static Country VNM => vnm.Value;

    /// <summary>
    /// Габон.
    /// </summary>
    public static Country GAB => gab.Value;

    /// <summary>
    /// Республика Гаити.
    /// </summary>
    public static Country HTI => hti.Value;

    /// <summary>
    /// Гайана.
    /// </summary>
    public static Country GUY => guy.Value;

    /// <summary>
    /// Гамбия.
    /// </summary>
    public static Country GMB => gmb.Value;

    /// <summary>
    /// Гана.
    /// </summary>
    public static Country GHA => gha.Value;

    /// <summary>
    /// Гваделупа.
    /// </summary>
    public static Country GLP => glp.Value;

    /// <summary>
    /// Гватемала.
    /// </summary>
    public static Country GTM => gtm.Value;

    /// <summary>
    /// Гвиана (департамент Франции).
    /// </summary>
    public static Country GUF => guf.Value;

    /// <summary>
    /// Гвинея.
    /// </summary>
    public static Country GIN => gin.Value;

    /// <summary>
    /// Гвинея-Бисау.
    /// </summary>
    public static Country GNB => gnb.Value;

    /// <summary>
    /// Германия.
    /// </summary>
    public static Country DEU => deu.Value;

    /// <summary>
    /// Гернси (коронное владение).
    /// </summary>
    public static Country GGY => ggy.Value;

    /// <summary>
    /// Гибралтар.
    /// </summary>
    public static Country GIB => gib.Value;

    /// <summary>
    /// Гондурас.
    /// </summary>
    public static Country HND => hnd.Value;

    /// <summary>
    /// Гонконг.
    /// </summary>
    public static Country HKG => hkg.Value;

    /// <summary>
    /// Гренада.
    /// </summary>
    public static Country GRD => grd.Value;

    /// <summary>
    /// Гренландия (административная единица).
    /// </summary>
    public static Country GRL => grl.Value;

    /// <summary>
    /// Греция.
    /// </summary>
    public static Country GRC => grc.Value;

    /// <summary>
    /// Грузия.
    /// </summary>
    public static Country GEO => geo.Value;

    /// <summary>
    /// Гуам.
    /// </summary>
    public static Country GUM => gum.Value;

    /// <summary>
    /// Дания.
    /// </summary>
    public static Country DNK => dnk.Value;

    /// <summary>
    /// Джерси (остров).
    /// </summary>
    public static Country JEY => jey.Value;

    /// <summary>
    /// Джибути.
    /// </summary>
    public static Country DJI => dji.Value;

    /// <summary>
    /// Доминика.
    /// </summary>
    public static Country DMA => dma.Value;

    /// <summary>
    /// Доминиканская Республика.
    /// </summary>
    public static Country DOM => dom.Value;

    /// <summary>
    /// Демократическая Республика Конго.
    /// </summary>
    public static Country COD => cod.Value;

    /// <summary>
    /// Египет.
    /// </summary>
    public static Country EGY => egy.Value;

    /// <summary>
    /// Замбия.
    /// </summary>
    public static Country ZMB => zmb.Value;

    /// <summary>
    /// Сахарская Арабская Демократическая Республика.
    /// </summary>
    public static Country ESH => esh.Value;

    /// <summary>
    /// Зимбабве.
    /// </summary>
    public static Country ZWE => zwe.Value;

    /// <summary>
    /// Израиль.
    /// </summary>
    public static Country ISR => isr.Value;

    /// <summary>
    /// Индия.
    /// </summary>
    public static Country IND => ind.Value;

    /// <summary>
    /// Индонезия.
    /// </summary>
    public static Country IDN => idn.Value;

    /// <summary>
    /// Иордания.
    /// </summary>
    public static Country JOR => jor.Value;

    /// <summary>
    /// Ирак.
    /// </summary>
    public static Country IRQ => irq.Value;

    /// <summary>
    /// Иран.
    /// </summary>
    public static Country IRN => irn.Value;

    /// <summary>
    /// Ирландия.
    /// </summary>
    public static Country IRL => irl.Value;

    /// <summary>
    /// Исландия.
    /// </summary>
    public static Country ISL => isl.Value;

    /// <summary>
    /// Испания.
    /// </summary>
    public static Country ESP => esp.Value;

    /// <summary>
    /// Италия.
    /// </summary>
    public static Country ITA => ita.Value;

    /// <summary>
    /// Йемен.
    /// </summary>
    public static Country YEM => yem.Value;

    /// <summary>
    /// Кабо-Верде.
    /// </summary>
    public static Country CPV => cpv.Value;

    /// <summary>
    /// Казахстан.
    /// </summary>
    public static Country KAZ => kaz.Value;

    /// <summary>
    /// Острова Кайман.
    /// </summary>
    public static Country CYM => cym.Value;

    /// <summary>
    /// Камбоджа.
    /// </summary>
    public static Country KHM => khm.Value;

    /// <summary>
    /// Камерун.
    /// </summary>
    public static Country CMR => cmr.Value;

    /// <summary>
    /// Канада.
    /// </summary>
    public static Country CAN => can.Value;

    /// <summary>
    /// Катар.
    /// </summary>
    public static Country QAT => qat.Value;

    /// <summary>
    /// Кения.
    /// </summary>
    public static Country KEN => ken.Value;

    /// <summary>
    /// Республика Кипр.
    /// </summary>
    public static Country CYP => cyp.Value;

    /// <summary>
    /// Кыргызстан.
    /// </summary>
    public static Country KGZ => kgz.Value;

    /// <summary>
    /// Кирибати.
    /// </summary>
    public static Country KIR => kir.Value;

    /// <summary>
    /// Китайская Республика (Тайвань).
    /// </summary>
    public static Country TWN => twn.Value;

    /// <summary>
    /// Корейская Народно-Демократическая Республика.
    /// </summary>
    public static Country PRK => prk.Value;

    /// <summary>
    /// Китай.
    /// </summary>
    public static Country CHN => chn.Value;

    /// <summary>
    /// Кокосовые острова.
    /// </summary>
    public static Country CCK => cck.Value;

    /// <summary>
    /// Колумбия.
    /// </summary>
    public static Country COL => col.Value;

    /// <summary>
    /// Коморы.
    /// </summary>
    public static Country COM => com.Value;

    /// <summary>
    /// Коста-Рика.
    /// </summary>
    public static Country CRI => cri.Value;

    /// <summary>
    /// Кот-д’Ивуар.
    /// </summary>
    public static Country CIV => civ.Value;

    /// <summary>
    /// Куба.
    /// </summary>
    public static Country CUB => cub.Value;

    /// <summary>
    /// Кувейт.
    /// </summary>
    public static Country KWT => kwt.Value;

    /// <summary>
    /// Кюрасао.
    /// </summary>
    public static Country CUW => cuw.Value;

    /// <summary>
    /// Лаос.
    /// </summary>
    public static Country LAO => lao.Value;

    /// <summary>
    /// Латвия.
    /// </summary>
    public static Country LVA => lva.Value;

    /// <summary>
    /// Лесото.
    /// </summary>
    public static Country LSO => lso.Value;

    /// <summary>
    /// Либерия.
    /// </summary>
    public static Country LBR => lbr.Value;

    /// <summary>
    /// Ливан.
    /// </summary>
    public static Country LBN => lbn.Value;

    /// <summary>
    /// Ливия.
    /// </summary>
    public static Country LBY => lby.Value;

    /// <summary>
    /// Литва.
    /// </summary>
    public static Country LTU => ltu.Value;

    /// <summary>
    /// Лихтенштейн.
    /// </summary>
    public static Country LIE => lie.Value;

    /// <summary>
    /// Люксембург.
    /// </summary>
    public static Country LUX => lux.Value;

    /// <summary>
    /// Маврикий.
    /// </summary>
    public static Country MUS => mus.Value;

    /// <summary>
    /// Мавритания.
    /// </summary>
    public static Country MRT => mrt.Value;

    /// <summary>
    /// Мадагаскар.
    /// </summary>
    public static Country MDG => mdg.Value;

    /// <summary>
    /// Майотта.
    /// </summary>
    public static Country MYT => myt.Value;

    /// <summary>
    /// Макао.
    /// </summary>
    public static Country MAC => mac.Value;

    /// <summary>
    /// Северная Македония.
    /// </summary>
    public static Country MKD => mkd.Value;

    /// <summary>
    /// Малави.
    /// </summary>
    public static Country MWI => mwi.Value;

    /// <summary>
    /// Малайзия.
    /// </summary>
    public static Country MYS => mys.Value;

    /// <summary>
    /// Мали.
    /// </summary>
    public static Country MLI => mli.Value;

    /// <summary>
    /// Мальдивы.
    /// </summary>
    public static Country MDV => mdv.Value;

    /// <summary>
    /// Мальта.
    /// </summary>
    public static Country MLT => mlt.Value;

    /// <summary>
    /// Марокко.
    /// </summary>
    public static Country MAR => mar.Value;

    /// <summary>
    /// Мартиника.
    /// </summary>
    public static Country MTQ => mtq.Value;

    /// <summary>
    /// Маршалловы Острова.
    /// </summary>
    public static Country MHL => mhl.Value;

    /// <summary>
    /// Мексика.
    /// </summary>
    public static Country MEX => mex.Value;

    /// <summary>
    /// Федеративные Штаты Микронезии.
    /// </summary>
    public static Country FSM => fsm.Value;

    /// <summary>
    /// Мозамбик.
    /// </summary>
    public static Country MOZ => moz.Value;

    /// <summary>
    /// Молдавия.
    /// </summary>
    public static Country MDA => mda.Value;

    /// <summary>
    /// Монако.
    /// </summary>
    public static Country MCO => mco.Value;

    /// <summary>
    /// Монголия.
    /// </summary>
    public static Country MNG => mng.Value;

    /// <summary>
    /// Монтсеррат.
    /// </summary>
    public static Country MSR => msr.Value;

    /// <summary>
    /// Мьянма.
    /// </summary>
    public static Country MMR => mmr.Value;

    /// <summary>
    /// Намибия.
    /// </summary>
    public static Country NAM => nam.Value;

    /// <summary>
    /// Науру.
    /// </summary>
    public static Country NRU => nru.Value;

    /// <summary>
    /// Непал.
    /// </summary>
    public static Country NPL => npl.Value;

    /// <summary>
    /// Нигер.
    /// </summary>
    public static Country NER => ner.Value;

    /// <summary>
    /// Нигерия.
    /// </summary>
    public static Country NGA => nga.Value;

    /// <summary>
    /// Нидерланды.
    /// </summary>
    public static Country NLD => nld.Value;

    /// <summary>
    /// Никарагуа.
    /// </summary>
    public static Country NIC => nic.Value;

    /// <summary>
    /// Ниуэ.
    /// </summary>
    public static Country NIU => niu.Value;

    /// <summary>
    /// Новая Зеландия.
    /// </summary>
    public static Country NZL => nzl.Value;

    /// <summary>
    /// Новая Каледония.
    /// </summary>
    public static Country NCL => ncl.Value;

    /// <summary>
    /// Норвегия.
    /// </summary>
    public static Country NOR => nor.Value;

    /// <summary>
    /// Объединённые Арабские Эмираты.
    /// </summary>
    public static Country ARE => are.Value;

    /// <summary>
    /// Оман.
    /// </summary>
    public static Country OMN => omn.Value;

    /// <summary>
    /// Остров Буве.
    /// </summary>
    public static Country BVT => bvt.Value;

    /// <summary>
    /// Остров Мэн.
    /// </summary>
    public static Country IMN => imn.Value;

    /// <summary>
    /// Острова Кука.
    /// </summary>
    public static Country COK => cok.Value;

    /// <summary>
    /// Остров Норфолк.
    /// </summary>
    public static Country NFK => nfk.Value;

    /// <summary>
    /// Остров Рождества (Австралия).
    /// </summary>
    public static Country CXR => cxr.Value;

    /// <summary>
    /// Острова Питкэрн.
    /// </summary>
    public static Country PCN => pcn.Value;

    /// <summary>
    /// Остров Святой Елены.
    /// </summary>
    public static Country SHN => shn.Value;

    /// <summary>
    /// Пакистан.
    /// </summary>
    public static Country PAK => pak.Value;

    /// <summary>
    /// Палау.
    /// </summary>
    public static Country PLW => plw.Value;

    /// <summary>
    /// Государство Палестина.
    /// </summary>
    public static Country PSE => pse.Value;

    /// <summary>
    /// Панама.
    /// </summary>
    public static Country PAN => pan.Value;

    /// <summary>
    /// Папуа — Новая Гвинея.
    /// </summary>
    public static Country PNG => png.Value;

    /// <summary>
    /// Парагвай.
    /// </summary>
    public static Country PRY => pry.Value;

    /// <summary>
    /// Перу.
    /// </summary>
    public static Country PER => per.Value;

    /// <summary>
    /// Польша.
    /// </summary>
    public static Country POL => pol.Value;

    /// <summary>
    /// Португалия.
    /// </summary>
    public static Country PRT => prt.Value;

    /// <summary>
    /// Пуэрто-Рико.
    /// </summary>
    public static Country PRI => pri.Value;

    /// <summary>
    /// Республика Конго.
    /// </summary>
    public static Country COG => cog.Value;

    /// <summary>
    /// Республика Корея.
    /// </summary>
    public static Country KOR => kor.Value;

    /// <summary>
    /// Реюньон.
    /// </summary>
    public static Country REU => reu.Value;

    /// <summary>
    /// Россия.
    /// </summary>
    public static Country RUS => rus.Value;

    /// <summary>
    /// Руанда.
    /// </summary>
    public static Country RWA => rwa.Value;

    /// <summary>
    /// Румыния.
    /// </summary>
    public static Country ROU => rou.Value;

    /// <summary>
    /// Сальвадор.
    /// </summary>
    public static Country SLV => slv.Value;

    /// <summary>
    /// Самоа.
    /// </summary>
    public static Country WSM => wsm.Value;

    /// <summary>
    /// Сан-Марино.
    /// </summary>
    public static Country SMR => smr.Value;

    /// <summary>
    /// Сан-Томе и Принсипи.
    /// </summary>
    public static Country STP => stp.Value;

    /// <summary>
    /// Саудовская Аравия.
    /// </summary>
    public static Country SAU => sau.Value;

    /// <summary>
    /// Эсватини.
    /// </summary>
    public static Country SWZ => swz.Value;

    /// <summary>
    /// Северные Марианские Острова.
    /// </summary>
    public static Country MNP => mnp.Value;

    /// <summary>
    /// Сейшельские Острова.
    /// </summary>
    public static Country SYC => syc.Value;

    /// <summary>
    /// Сен-Бартелеми (заморское сообщество).
    /// </summary>
    public static Country BLM => blm.Value;

    /// <summary>
    /// Сен-Мартен (владение Франции).
    /// </summary>
    public static Country MAF => maf.Value;

    /// <summary>
    /// Сен-Пьер и Микелон.
    /// </summary>
    public static Country SPM => spm.Value;

    /// <summary>
    /// Сенегал.
    /// </summary>
    public static Country SEN => sen.Value;

    /// <summary>
    /// Сент-Винсент и Гренадины.
    /// </summary>
    public static Country VCT => vct.Value;

    /// <summary>
    /// Сент-Китс и Невис.
    /// </summary>
    public static Country KNA => kna.Value;

    /// <summary>
    /// Сент-Люсия.
    /// </summary>
    public static Country LCA => lca.Value;

    /// <summary>
    /// Сербия.
    /// </summary>
    public static Country SRB => srb.Value;

    /// <summary>
    /// Сингапур.
    /// </summary>
    public static Country SGP => sgp.Value;

    /// <summary>
    /// Синт-Мартен.
    /// </summary>
    public static Country SXM => sxm.Value;

    /// <summary>
    /// Сирия.
    /// </summary>
    public static Country SYR => syr.Value;

    /// <summary>
    /// Словакия.
    /// </summary>
    public static Country SVK => svk.Value;

    /// <summary>
    /// Словения.
    /// </summary>
    public static Country SVN => svn.Value;

    /// <summary>
    /// Соломоновы Острова.
    /// </summary>
    public static Country SLB => slb.Value;

    /// <summary>
    /// Сомали.
    /// </summary>
    public static Country SOM => som.Value;

    /// <summary>
    /// Судан.
    /// </summary>
    public static Country SDN => sdn.Value;

    /// <summary>
    /// Суринам.
    /// </summary>
    public static Country SUR => sur.Value;

    /// <summary>
    /// Соединённые Штаты Америки.
    /// </summary>
    public static Country USA => usa.Value;

    /// <summary>
    /// Сьерра-Леоне.
    /// </summary>
    public static Country SLE => sle.Value;

    /// <summary>
    /// Таджикистан.
    /// </summary>
    public static Country TJK => tjk.Value;

    /// <summary>
    /// Таиланд.
    /// </summary>
    public static Country THA => tha.Value;

    /// <summary>
    /// Танзания.
    /// </summary>
    public static Country TZA => tza.Value;

    /// <summary>
    /// Теркс и Кайкос.
    /// </summary>
    public static Country TCA => tca.Value;

    /// <summary>
    /// Того.
    /// </summary>
    public static Country TGO => tgo.Value;

    /// <summary>
    /// Токелау.
    /// </summary>
    public static Country TKL => tkl.Value;

    /// <summary>
    /// Тонга.
    /// </summary>
    public static Country TON => ton.Value;

    /// <summary>
    /// Тринидад и Тобаго.
    /// </summary>
    public static Country TTO => tto.Value;

    /// <summary>
    /// Тувалу.
    /// </summary>
    public static Country TUV => tuv.Value;

    /// <summary>
    /// Тунис.
    /// </summary>
    public static Country TUN => tun.Value;

    /// <summary>
    /// Туркменистан.
    /// </summary>
    public static Country TKM => tkm.Value;

    /// <summary>
    /// Турция.
    /// </summary>
    public static Country TUR => tur.Value;

    /// <summary>
    /// Уганда.
    /// </summary>
    public static Country UGA => uga.Value;

    /// <summary>
    /// Узбекистан.
    /// </summary>
    public static Country UZB => uzb.Value;

    /// <summary>
    /// Украина.
    /// </summary>
    public static Country UKR => ukr.Value;

    /// <summary>
    /// Уоллис и Футуна.
    /// </summary>
    public static Country WLF => wlf.Value;

    /// <summary>
    /// Уругвай.
    /// </summary>
    public static Country URY => ury.Value;

    /// <summary>
    /// Фарерские острова.
    /// </summary>
    public static Country FRO => fro.Value;

    /// <summary>
    /// Фиджи.
    /// </summary>
    public static Country FJI => fji.Value;

    /// <summary>
    /// Филиппины.
    /// </summary>
    public static Country PHL => phl.Value;

    /// <summary>
    /// Финляндия.
    /// </summary>
    public static Country FIN => fin.Value;

    /// <summary>
    /// Фолклендские острова.
    /// </summary>
    public static Country FLK => flk.Value;

    /// <summary>
    /// Франция.
    /// </summary>
    public static Country FRA => fra.Value;

    /// <summary>
    /// Французская Полинезия.
    /// </summary>
    public static Country PYF => pyf.Value;

    /// <summary>
    /// Французские Южные и Антарктические территории.
    /// </summary>
    public static Country ATF => atf.Value;

    /// <summary>
    /// Остров Херд и острова Макдональд.
    /// </summary>
    public static Country HMD => hmd.Value;

    /// <summary>
    /// Хорватия.
    /// </summary>
    public static Country HRV => hrv.Value;

    /// <summary>
    /// Центральноафриканская Республика.
    /// </summary>
    public static Country CAF => caf.Value;

    /// <summary>
    /// Чад.
    /// </summary>
    public static Country TCD => tcd.Value;

    /// <summary>
    /// Черногория.
    /// </summary>
    public static Country MNE => mne.Value;

    /// <summary>
    /// Чехия.
    /// </summary>
    public static Country CZE => cze.Value;

    /// <summary>
    /// Чили.
    /// </summary>
    public static Country CHL => chl.Value;

    /// <summary>
    /// Швейцария.
    /// </summary>
    public static Country CHE => che.Value;

    /// <summary>
    /// Швеция.
    /// </summary>
    public static Country SWE => swe.Value;

    /// <summary>
    /// Флаг Шпицбергена и Ян-Майена.
    /// </summary>
    public static Country SJM => sjm.Value;

    /// <summary>
    /// Шри-Ланка.
    /// </summary>
    public static Country LKA => lka.Value;

    /// <summary>
    /// Эквадор.
    /// </summary>
    public static Country ECU => ecu.Value;

    /// <summary>
    /// Экваториальная Гвинея.
    /// </summary>
    public static Country GNQ => gnq.Value;

    /// <summary>
    /// Эритрея.
    /// </summary>
    public static Country ERI => eri.Value;

    /// <summary>
    /// Эстония.
    /// </summary>
    public static Country EST => est.Value;

    /// <summary>
    /// Эфиопия.
    /// </summary>
    public static Country ETH => eth.Value;

    /// <summary>
    /// Южно-Африканская Республика.
    /// </summary>
    public static Country ZAF => zaf.Value;

    /// <summary>
    /// Южная Георгия и Южные Сандвичевы Острова.
    /// </summary>
    public static Country SGS => sgs.Value;

    /// <summary>
    /// Южный Судан.
    /// </summary>
    public static Country SSD => ssd.Value;

    /// <summary>
    /// Ямайка.
    /// </summary>
    public static Country JAM => jam.Value;

    /// <summary>
    /// Япония.
    /// </summary>
    public static Country JPN => jpn.Value;

    #endregion

    #region Коды (двухсимвольные)

    /// <summary>
    /// Австралия.
    /// </summary>
    public static Country AU => aus.Value;

    /// <summary>
    /// Австрия.
    /// </summary>
    public static Country AT => aut.Value;

    /// <summary>
    /// Азербайджан.
    /// </summary>
    public static Country AZ => aze.Value;

    /// <summary>
    /// Аландские острова.
    /// </summary>
    public static Country AX => ala.Value;

    /// <summary>
    /// Албания.
    /// </summary>
    public static Country AL => alb.Value;

    /// <summary>
    /// Алжир.
    /// </summary>
    public static Country DZ => dza.Value;

    /// <summary>
    /// Виргинские Острова (США).
    /// </summary>
    public static Country VI => vir.Value;

    /// <summary>
    /// Американское Самоа.
    /// </summary>
    public static Country AS => asm.Value;

    /// <summary>
    /// Ангилья.
    /// </summary>
    public static Country AI => aia.Value;

    /// <summary>
    /// Ангола.
    /// </summary>
    public static Country AO => ago.Value;

    /// <summary>
    /// Андорра.
    /// </summary>
    public static Country AD => and.Value;

    /// <summary>
    /// Антарктика.
    /// </summary>
    public static Country AQ => ata.Value;

    /// <summary>
    /// Антигуа и Барбуда.
    /// </summary>
    public static Country AG => atg.Value;

    /// <summary>
    /// Аргентина.
    /// </summary>
    public static Country AR => arg.Value;

    /// <summary>
    /// Армения.
    /// </summary>
    public static Country AM => arm.Value;

    /// <summary>
    /// Аруба.
    /// </summary>
    public static Country AW => abw.Value;

    /// <summary>
    /// Афганистан.
    /// </summary>
    public static Country AF => afg.Value;

    /// <summary>
    /// Багамские Острова.
    /// </summary>
    public static Country BS => bhs.Value;

    /// <summary>
    /// Бангладеш.
    /// </summary>
    public static Country BD => bgd.Value;

    /// <summary>
    /// Барбадос.
    /// </summary>
    public static Country BB => brb.Value;

    /// <summary>
    /// Бахрейн.
    /// </summary>
    public static Country BH => bhr.Value;

    /// <summary>
    /// Белиз.
    /// </summary>
    public static Country BZ => blz.Value;

    /// <summary>
    /// Белоруссия.
    /// </summary>
    public static Country BY => blr.Value;

    /// <summary>
    /// Бельгия.
    /// </summary>
    public static Country BE => bel.Value;

    /// <summary>
    /// Бенин.
    /// </summary>
    public static Country BJ => ben.Value;

    /// <summary>
    /// Бермудские Острова.
    /// </summary>
    public static Country BM => bmu.Value;

    /// <summary>
    /// Болгария.
    /// </summary>
    public static Country BG => bgr.Value;

    /// <summary>
    /// Боливия.
    /// </summary>
    public static Country BO => bol.Value;

    /// <summary>
    /// Бонайре, Синт-Эстатиус и Саба.
    /// </summary>
    public static Country BQ => bes.Value;

    /// <summary>
    /// Босния и Герцеговина.
    /// </summary>
    public static Country BA => bih.Value;

    /// <summary>
    /// Ботсвана.
    /// </summary>
    public static Country BW => bwa.Value;

    /// <summary>
    /// Бразилия.
    /// </summary>
    public static Country BR => bra.Value;

    /// <summary>
    /// Британская Территория в Индийском Океане.
    /// </summary>
    public static Country IO => iot.Value;

    /// <summary>
    /// Виргинские Острова (Великобритания).
    /// </summary>
    public static Country VG => vgb.Value;

    /// <summary>
    /// Бруней.
    /// </summary>
    public static Country BN => brn.Value;

    /// <summary>
    /// Буркина-Фасо.
    /// </summary>
    public static Country BF => bfa.Value;

    /// <summary>
    /// Бурунди.
    /// </summary>
    public static Country BI => bdi.Value;

    /// <summary>
    /// Бутан.
    /// </summary>
    public static Country BT => btn.Value;

    /// <summary>
    /// Вануату.
    /// </summary>
    public static Country VU => vut.Value;

    /// <summary>
    /// Ватикан.
    /// </summary>
    public static Country VA => vat.Value;

    /// <summary>
    /// Великобритания.
    /// </summary>
    public static Country GB => gbr.Value;

    /// <summary>
    /// Венгрия.
    /// </summary>
    public static Country HU => hun.Value;

    /// <summary>
    /// Венесуэла.
    /// </summary>
    public static Country VE => ven.Value;

    /// <summary>
    /// Внешние малые острова США.
    /// </summary>
    public static Country UM => umi.Value;

    /// <summary>
    /// Восточный Тимор.
    /// </summary>
    public static Country TL => tls.Value;

    /// <summary>
    /// Вьетнам.
    /// </summary>
    public static Country VN => vnm.Value;

    /// <summary>
    /// Габон.
    /// </summary>
    public static Country GA => gab.Value;

    /// <summary>
    /// Республика Гаити.
    /// </summary>
    public static Country HT => hti.Value;

    /// <summary>
    /// Гайана.
    /// </summary>
    public static Country GY => guy.Value;

    /// <summary>
    /// Гамбия.
    /// </summary>
    public static Country GM => gmb.Value;

    /// <summary>
    /// Гана.
    /// </summary>
    public static Country GH => gha.Value;

    /// <summary>
    /// Гваделупа.
    /// </summary>
    public static Country GP => glp.Value;

    /// <summary>
    /// Гватемала.
    /// </summary>
    public static Country GT => gtm.Value;

    /// <summary>
    /// Гвиана (департамент Франции).
    /// </summary>
    public static Country GF => guf.Value;

    /// <summary>
    /// Гвинея.
    /// </summary>
    public static Country GN => gin.Value;

    /// <summary>
    /// Гвинея-Бисау.
    /// </summary>
    public static Country GW => gnb.Value;

    /// <summary>
    /// Германия.
    /// </summary>
    public static Country DE => deu.Value;

    /// <summary>
    /// Гернси (коронное владение).
    /// </summary>
    public static Country GG => ggy.Value;

    /// <summary>
    /// Гибралтар.
    /// </summary>
    public static Country GI => gib.Value;

    /// <summary>
    /// Гондурас.
    /// </summary>
    public static Country HN => hnd.Value;

    /// <summary>
    /// Гонконг.
    /// </summary>
    public static Country HK => hkg.Value;

    /// <summary>
    /// Гренада.
    /// </summary>
    public static Country GD => grd.Value;

    /// <summary>
    /// Гренландия (административная единица).
    /// </summary>
    public static Country GL => grl.Value;

    /// <summary>
    /// Греция.
    /// </summary>
    public static Country GR => grc.Value;

    /// <summary>
    /// Грузия.
    /// </summary>
    public static Country GE => geo.Value;

    /// <summary>
    /// Гуам.
    /// </summary>
    public static Country GU => gum.Value;

    /// <summary>
    /// Дания.
    /// </summary>
    public static Country DK => dnk.Value;

    /// <summary>
    /// Джерси (остров).
    /// </summary>
    public static Country JE => jey.Value;

    /// <summary>
    /// Джибути.
    /// </summary>
    public static Country DJ => dji.Value;

    /// <summary>
    /// Доминика.
    /// </summary>
    public static Country DM => dma.Value;

    /// <summary>
    /// Доминиканская Республика.
    /// </summary>
    public static Country DO => dom.Value;

    /// <summary>
    /// Демократическая Республика Конго.
    /// </summary>
    public static Country CD => cod.Value;

    /// <summary>
    /// Египет.
    /// </summary>
    public static Country EG => egy.Value;

    /// <summary>
    /// Замбия.
    /// </summary>
    public static Country ZM => zmb.Value;

    /// <summary>
    /// Сахарская Арабская Демократическая Республика.
    /// </summary>
    public static Country EH => esh.Value;

    /// <summary>
    /// Зимбабве.
    /// </summary>
    public static Country ZW => zwe.Value;

    /// <summary>
    /// Израиль.
    /// </summary>
    public static Country IL => isr.Value;

    /// <summary>
    /// Индия.
    /// </summary>
    public static Country IN => ind.Value;

    /// <summary>
    /// Индонезия.
    /// </summary>
    public static Country ID => idn.Value;

    /// <summary>
    /// Иордания.
    /// </summary>
    public static Country JO => jor.Value;

    /// <summary>
    /// Ирак.
    /// </summary>
    public static Country IQ => irq.Value;

    /// <summary>
    /// Иран.
    /// </summary>
    public static Country IR => irn.Value;

    /// <summary>
    /// Ирландия.
    /// </summary>
    public static Country IE => irl.Value;

    /// <summary>
    /// Исландия.
    /// </summary>
    public static Country IS => isl.Value;

    /// <summary>
    /// Испания.
    /// </summary>
    public static Country ES => esp.Value;

    /// <summary>
    /// Италия.
    /// </summary>
    public static Country IT => ita.Value;

    /// <summary>
    /// Йемен.
    /// </summary>
    public static Country YE => yem.Value;

    /// <summary>
    /// Кабо-Верде.
    /// </summary>
    public static Country CV => cpv.Value;

    /// <summary>
    /// Казахстан.
    /// </summary>
    public static Country KZ => kaz.Value;

    /// <summary>
    /// Острова Кайман.
    /// </summary>
    public static Country KY => cym.Value;

    /// <summary>
    /// Камбоджа.
    /// </summary>
    public static Country KH => khm.Value;

    /// <summary>
    /// Камерун.
    /// </summary>
    public static Country CM => cmr.Value;

    /// <summary>
    /// Канада.
    /// </summary>
    public static Country CA => can.Value;

    /// <summary>
    /// Катар.
    /// </summary>
    public static Country QA => qat.Value;

    /// <summary>
    /// Кения.
    /// </summary>
    public static Country KE => ken.Value;

    /// <summary>
    /// Республика Кипр.
    /// </summary>
    public static Country CY => cyp.Value;

    /// <summary>
    /// Кыргызстан.
    /// </summary>
    public static Country KG => kgz.Value;

    /// <summary>
    /// Кирибати.
    /// </summary>
    public static Country KI => kir.Value;

    /// <summary>
    /// Китайская Республика (Тайвань).
    /// </summary>
    public static Country TW => twn.Value;

    /// <summary>
    /// Корейская Народно-Демократическая Республика.
    /// </summary>
    public static Country KP => prk.Value;

    /// <summary>
    /// Китай.
    /// </summary>
    public static Country CN => chn.Value;

    /// <summary>
    /// Кокосовые острова.
    /// </summary>
    public static Country CC => cck.Value;

    /// <summary>
    /// Колумбия.
    /// </summary>
    public static Country CO => col.Value;

    /// <summary>
    /// Коморы.
    /// </summary>
    public static Country KM => com.Value;

    /// <summary>
    /// Коста-Рика.
    /// </summary>
    public static Country CR => cri.Value;

    /// <summary>
    /// Кот-д’Ивуар.
    /// </summary>
    public static Country CI => civ.Value;

    /// <summary>
    /// Куба.
    /// </summary>
    public static Country CU => cub.Value;

    /// <summary>
    /// Кувейт.
    /// </summary>
    public static Country KW => kwt.Value;

    /// <summary>
    /// Кюрасао.
    /// </summary>
    public static Country CW => cuw.Value;

    /// <summary>
    /// Лаос.
    /// </summary>
    public static Country LA => lao.Value;

    /// <summary>
    /// Латвия.
    /// </summary>
    public static Country LV => lva.Value;

    /// <summary>
    /// Лесото.
    /// </summary>
    public static Country LS => lso.Value;

    /// <summary>
    /// Либерия.
    /// </summary>
    public static Country LR => lbr.Value;

    /// <summary>
    /// Ливан.
    /// </summary>
    public static Country LB => lbn.Value;

    /// <summary>
    /// Ливия.
    /// </summary>
    public static Country LY => lby.Value;

    /// <summary>
    /// Литва.
    /// </summary>
    public static Country LT => ltu.Value;

    /// <summary>
    /// Лихтенштейн.
    /// </summary>
    public static Country LI => lie.Value;

    /// <summary>
    /// Люксембург.
    /// </summary>
    public static Country LU => lux.Value;

    /// <summary>
    /// Маврикий.
    /// </summary>
    public static Country MU => mus.Value;

    /// <summary>
    /// Мавритания.
    /// </summary>
    public static Country MR => mrt.Value;

    /// <summary>
    /// Мадагаскар.
    /// </summary>
    public static Country MG => mdg.Value;

    /// <summary>
    /// Майотта.
    /// </summary>
    public static Country YT => myt.Value;

    /// <summary>
    /// Макао.
    /// </summary>
    public static Country MO => mac.Value;

    /// <summary>
    /// Северная Македония.
    /// </summary>
    public static Country MK => mkd.Value;

    /// <summary>
    /// Малави.
    /// </summary>
    public static Country MW => mwi.Value;

    /// <summary>
    /// Малайзия.
    /// </summary>
    public static Country MY => mys.Value;

    /// <summary>
    /// Мали.
    /// </summary>
    public static Country ML => mli.Value;

    /// <summary>
    /// Мальдивы.
    /// </summary>
    public static Country MV => mdv.Value;

    /// <summary>
    /// Мальта.
    /// </summary>
    public static Country MT => mlt.Value;

    /// <summary>
    /// Марокко.
    /// </summary>
    public static Country MA => mar.Value;

    /// <summary>
    /// Мартиника.
    /// </summary>
    public static Country MQ => mtq.Value;

    /// <summary>
    /// Маршалловы Острова.
    /// </summary>
    public static Country MH => mhl.Value;

    /// <summary>
    /// Мексика.
    /// </summary>
    public static Country MX => mex.Value;

    /// <summary>
    /// Федеративные Штаты Микронезии.
    /// </summary>
    public static Country FM => fsm.Value;

    /// <summary>
    /// Мозамбик.
    /// </summary>
    public static Country MZ => moz.Value;

    /// <summary>
    /// Молдавия.
    /// </summary>
    public static Country MD => mda.Value;

    /// <summary>
    /// Монако.
    /// </summary>
    public static Country MC => mco.Value;

    /// <summary>
    /// Монголия.
    /// </summary>
    public static Country MN => mng.Value;

    /// <summary>
    /// Монтсеррат.
    /// </summary>
    public static Country MS => msr.Value;

    /// <summary>
    /// Мьянма.
    /// </summary>
    public static Country MM => mmr.Value;

    /// <summary>
    /// Намибия.
    /// </summary>
    public static Country NA => nam.Value;

    /// <summary>
    /// Науру.
    /// </summary>
    public static Country NR => nru.Value;

    /// <summary>
    /// Непал.
    /// </summary>
    public static Country NP => npl.Value;

    /// <summary>
    /// Нигер.
    /// </summary>
    public static Country NE => ner.Value;

    /// <summary>
    /// Нигерия.
    /// </summary>
    public static Country NG => nga.Value;

    /// <summary>
    /// Нидерланды.
    /// </summary>
    public static Country NL => nld.Value;

    /// <summary>
    /// Никарагуа.
    /// </summary>
    public static Country NI => nic.Value;

    /// <summary>
    /// Ниуэ.
    /// </summary>
    public static Country NU => niu.Value;

    /// <summary>
    /// Новая Зеландия.
    /// </summary>
    public static Country NZ => nzl.Value;

    /// <summary>
    /// Новая Каледония.
    /// </summary>
    public static Country NC => ncl.Value;

    /// <summary>
    /// Норвегия.
    /// </summary>
    public static Country NO => nor.Value;

    /// <summary>
    /// Объединённые Арабские Эмираты.
    /// </summary>
    public static Country AE => are.Value;

    /// <summary>
    /// Оман.
    /// </summary>
    public static Country OM => omn.Value;

    /// <summary>
    /// Остров Буве.
    /// </summary>
    public static Country BV => bvt.Value;

    /// <summary>
    /// Остров Мэн.
    /// </summary>
    public static Country IM => imn.Value;

    /// <summary>
    /// Острова Кука.
    /// </summary>
    public static Country CK => cok.Value;

    /// <summary>
    /// Остров Норфолк.
    /// </summary>
    public static Country NF => nfk.Value;

    /// <summary>
    /// Остров Рождества (Австралия).
    /// </summary>
    public static Country CX => cxr.Value;

    /// <summary>
    /// Острова Питкэрн.
    /// </summary>
    public static Country PN => pcn.Value;

    /// <summary>
    /// Остров Святой Елены.
    /// </summary>
    public static Country SH => shn.Value;

    /// <summary>
    /// Пакистан.
    /// </summary>
    public static Country PK => pak.Value;

    /// <summary>
    /// Палау.
    /// </summary>
    public static Country PW => plw.Value;

    /// <summary>
    /// Государство Палестина.
    /// </summary>
    public static Country PS => pse.Value;

    /// <summary>
    /// Панама.
    /// </summary>
    public static Country PA => pan.Value;

    /// <summary>
    /// Папуа — Новая Гвинея.
    /// </summary>
    public static Country PG => png.Value;

    /// <summary>
    /// Парагвай.
    /// </summary>
    public static Country PY => pry.Value;

    /// <summary>
    /// Перу.
    /// </summary>
    public static Country PE => per.Value;

    /// <summary>
    /// Польша.
    /// </summary>
    public static Country PL => pol.Value;

    /// <summary>
    /// Португалия.
    /// </summary>
    public static Country PT => prt.Value;

    /// <summary>
    /// Пуэрто-Рико.
    /// </summary>
    public static Country PR => pri.Value;

    /// <summary>
    /// Республика Конго.
    /// </summary>
    public static Country CG => cog.Value;

    /// <summary>
    /// Республика Корея.
    /// </summary>
    public static Country KR => kor.Value;

    /// <summary>
    /// Реюньон.
    /// </summary>
    public static Country RE => reu.Value;

    /// <summary>
    /// Россия.
    /// </summary>
    public static Country RU => rus.Value;

    /// <summary>
    /// Руанда.
    /// </summary>
    public static Country RW => rwa.Value;

    /// <summary>
    /// Румыния.
    /// </summary>
    public static Country RO => rou.Value;

    /// <summary>
    /// Сальвадор.
    /// </summary>
    public static Country SV => slv.Value;

    /// <summary>
    /// Самоа.
    /// </summary>
    public static Country WS => wsm.Value;

    /// <summary>
    /// Сан-Марино.
    /// </summary>
    public static Country SM => smr.Value;

    /// <summary>
    /// Сан-Томе и Принсипи.
    /// </summary>
    public static Country ST => stp.Value;

    /// <summary>
    /// Саудовская Аравия.
    /// </summary>
    public static Country SA => sau.Value;

    /// <summary>
    /// Эсватини.
    /// </summary>
    public static Country SZ => swz.Value;

    /// <summary>
    /// Северные Марианские Острова.
    /// </summary>
    public static Country MP => mnp.Value;

    /// <summary>
    /// Сейшельские Острова.
    /// </summary>
    public static Country SC => syc.Value;

    /// <summary>
    /// Сен-Бартелеми (заморское сообщество).
    /// </summary>
    public static Country BL => blm.Value;

    /// <summary>
    /// Сен-Мартен (владение Франции).
    /// </summary>
    public static Country MF => maf.Value;

    /// <summary>
    /// Сен-Пьер и Микелон.
    /// </summary>
    public static Country PM => spm.Value;

    /// <summary>
    /// Сенегал.
    /// </summary>
    public static Country SN => sen.Value;

    /// <summary>
    /// Сент-Винсент и Гренадины.
    /// </summary>
    public static Country VC => vct.Value;

    /// <summary>
    /// Сент-Китс и Невис.
    /// </summary>
    public static Country KN => kna.Value;

    /// <summary>
    /// Сент-Люсия.
    /// </summary>
    public static Country LC => lca.Value;

    /// <summary>
    /// Сербия.
    /// </summary>
    public static Country RS => srb.Value;

    /// <summary>
    /// Сингапур.
    /// </summary>
    public static Country SG => sgp.Value;

    /// <summary>
    /// Синт-Мартен.
    /// </summary>
    public static Country SX => sxm.Value;

    /// <summary>
    /// Сирия.
    /// </summary>
    public static Country SY => syr.Value;

    /// <summary>
    /// Словакия.
    /// </summary>
    public static Country SK => svk.Value;

    /// <summary>
    /// Словения.
    /// </summary>
    public static Country SI => svn.Value;

    /// <summary>
    /// Соломоновы Острова.
    /// </summary>
    public static Country SB => slb.Value;

    /// <summary>
    /// Сомали.
    /// </summary>
    public static Country SO => som.Value;

    /// <summary>
    /// Судан.
    /// </summary>
    public static Country SD => sdn.Value;

    /// <summary>
    /// Суринам.
    /// </summary>
    public static Country SR => sur.Value;

    /// <summary>
    /// Соединённые Штаты Америки.
    /// </summary>
    public static Country US => usa.Value;

    /// <summary>
    /// Сьерра-Леоне.
    /// </summary>
    public static Country SL => sle.Value;

    /// <summary>
    /// Таджикистан.
    /// </summary>
    public static Country TJ => tjk.Value;

    /// <summary>
    /// Таиланд.
    /// </summary>
    public static Country TH => tha.Value;

    /// <summary>
    /// Танзания.
    /// </summary>
    public static Country TZ => tza.Value;

    /// <summary>
    /// Теркс и Кайкос.
    /// </summary>
    public static Country TC => tca.Value;

    /// <summary>
    /// Того.
    /// </summary>
    public static Country TG => tgo.Value;

    /// <summary>
    /// Токелау.
    /// </summary>
    public static Country TK => tkl.Value;

    /// <summary>
    /// Тонга.
    /// </summary>
    public static Country TO => ton.Value;

    /// <summary>
    /// Тринидад и Тобаго.
    /// </summary>
    public static Country TT => tto.Value;

    /// <summary>
    /// Тувалу.
    /// </summary>
    public static Country TV => tuv.Value;

    /// <summary>
    /// Тунис.
    /// </summary>
    public static Country TN => tun.Value;

    /// <summary>
    /// Туркменистан.
    /// </summary>
    public static Country TM => tkm.Value;

    /// <summary>
    /// Турция.
    /// </summary>
    public static Country TR => tur.Value;

    /// <summary>
    /// Уганда.
    /// </summary>
    public static Country UG => uga.Value;

    /// <summary>
    /// Узбекистан.
    /// </summary>
    public static Country UZ => uzb.Value;

    /// <summary>
    /// Украина.
    /// </summary>
    public static Country UA => ukr.Value;

    /// <summary>
    /// Уоллис и Футуна.
    /// </summary>
    public static Country WF => wlf.Value;

    /// <summary>
    /// Уругвай.
    /// </summary>
    public static Country UY => ury.Value;

    /// <summary>
    /// Фарерские острова.
    /// </summary>
    public static Country FO => fro.Value;

    /// <summary>
    /// Фиджи.
    /// </summary>
    public static Country FJ => fji.Value;

    /// <summary>
    /// Филиппины.
    /// </summary>
    public static Country PH => phl.Value;

    /// <summary>
    /// Финляндия.
    /// </summary>
    public static Country FI => fin.Value;

    /// <summary>
    /// Фолклендские острова.
    /// </summary>
    public static Country FK => flk.Value;

    /// <summary>
    /// Франция.
    /// </summary>
    public static Country FR => fra.Value;

    /// <summary>
    /// Французская Полинезия.
    /// </summary>
    public static Country PF => pyf.Value;

    /// <summary>
    /// Французские Южные и Антарктические территории.
    /// </summary>
    public static Country TF => atf.Value;

    /// <summary>
    /// Остров Херд и острова Макдональд.
    /// </summary>
    public static Country HM => hmd.Value;

    /// <summary>
    /// Хорватия.
    /// </summary>
    public static Country HR => hrv.Value;

    /// <summary>
    /// Центральноафриканская Республика.
    /// </summary>
    public static Country CF => caf.Value;

    /// <summary>
    /// Чад.
    /// </summary>
    public static Country TD => tcd.Value;

    /// <summary>
    /// Черногория.
    /// </summary>
    public static Country ME => mne.Value;

    /// <summary>
    /// Чехия.
    /// </summary>
    public static Country CZ => cze.Value;

    /// <summary>
    /// Чили.
    /// </summary>
    public static Country CL => chl.Value;

    /// <summary>
    /// Швейцария.
    /// </summary>
    public static Country CH => che.Value;

    /// <summary>
    /// Швеция.
    /// </summary>
    public static Country SE => swe.Value;

    /// <summary>
    /// Флаг Шпицбергена и Ян-Майена.
    /// </summary>
    public static Country SJ => sjm.Value;

    /// <summary>
    /// Шри-Ланка.
    /// </summary>
    public static Country LK => lka.Value;

    /// <summary>
    /// Эквадор.
    /// </summary>
    public static Country EC => ecu.Value;

    /// <summary>
    /// Экваториальная Гвинея.
    /// </summary>
    public static Country GQ => gnq.Value;

    /// <summary>
    /// Эритрея.
    /// </summary>
    public static Country ER => eri.Value;

    /// <summary>
    /// Эстония.
    /// </summary>
    public static Country EE => est.Value;

    /// <summary>
    /// Эфиопия.
    /// </summary>
    public static Country ET => eth.Value;

    /// <summary>
    /// Южно-Африканская Республика.
    /// </summary>
    public static Country ZA => zaf.Value;

    /// <summary>
    /// Южная Георгия и Южные Сандвичевы Острова.
    /// </summary>
    public static Country GS => sgs.Value;

    /// <summary>
    /// Южный Судан.
    /// </summary>
    public static Country SS => ssd.Value;

    /// <summary>
    /// Ямайка.
    /// </summary>
    public static Country JM => jam.Value;

    /// <summary>
    /// Япония.
    /// </summary>
    public static Country JP => jpn.Value;

    #endregion

    /// <summary>
    /// Коллекция всех стран.
    /// </summary>
    public static IEnumerable<Country> All =>
    [
        AUS, AUT, AZE, ALA, ALB, DZA, VIR, ASM, AIA, AGO, AND, ATA, ATG, ARG, ARM, ABW, AFG, BHS, BGD, BRB, BHR, BLZ, BLR, BEL, BEN,
        BMU, BGR, BOL, BES, BIH, BWA, BRA, IOT, VGB, BRN, BFA, BDI, BTN, VUT, VAT, GBR, HUN, VEN, UMI, TLS, VNM, GAB, HTI, GUY, GMB,
        GHA, GLP, GTM, GUF, GIN, GNB, DEU, GGY, GIB, HND, HKG, GRD, GRL, GRC, GEO, GUM, DNK, JEY, DJI, DMA, DOM, COD, EGY, ZMB, ESH,
        ZWE, ISR, IND, IDN, JOR, IRQ, IRN, IRL, ISL, ESP, ITA, YEM, CPV, KAZ, CYM, KHM, CMR, CAN, QAT, KEN, CYP, KGZ, KIR, TWN, PRK,
        CHN, CCK, COL, COM, CRI, CIV, CUB, KWT, CUW, LAO, LVA, LSO, LBR, LBN, LBY, LTU, LIE, LUX, MUS, MRT, MDG, MYT, MAC, MKD, MWI,
        MYS, MLI, MDV, MLT, MAR, MTQ, MHL, MEX, FSM, MOZ, MDA, MCO, MNG, MSR, MMR, NAM, NRU, NPL, NER, NGA, NLD, NIC, NIU, NZL, NCL,
        NOR, ARE, OMN, BVT, IMN, COK, NFK, CXR, PCN, SHN, PAK, PLW, PSE, PAN, PNG, PRY, PER, POL, PRT, PRI, COG, KOR, REU, RUS, RWA,
        ROU, SLV, WSM, SMR, STP, SAU, SWZ, MNP, SYC, BLM, MAF, SPM, SEN, VCT, KNA, LCA, SRB, SGP, SXM, SYR, SVK, SVN, SLB, SOM, SDN,
        SUR, USA, SLE, TJK, THA, TZA, TCA, TGO, TKL, TON, TTO, TUV, TUN, TKM, TUR, UGA, UZB, UKR, WLF, URY, FRO, FJI, PHL, FIN, FLK,
        FRA, PYF, ATF, HMD, HRV, CAF, TCD, MNE, CZE, CHL, CHE, SWE, SJM, LKA, ECU, GNQ, ERI, EST, ETH, ZAF, SGS, SSD, JAM, JPN,
    ];

    #region Конструкторы

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Country"/>.
    /// </summary>
    /// <param name="name">Название (на русском).</param>
    /// <param name="internationalName">Название (интернациональное).</param>
    /// <param name="code">Цифровой код страны.</param>
    /// <param name="isoCode2">Двухсимвольный код страны.</param>
    /// <param name="isoCode3">Трёхсимвольный код страны.</param>
    /// <param name="timeZoneOffset">Смещение часового пояса.</param>
    /// <param name="currency">Основная валюта страны.</param>
    /// <param name="dialCode">Международный телефонный код страны.</param>
    /// <param name="domain">Домен.</param>
    /// <param name="ioc">Международный олимпийский код.</param>
    public Country(string name, string internationalName, ushort code, string isoCode2, string isoCode3, TimeSpan timeZoneOffset, Currency currency, ushort dialCode, string? domain, string? ioc)
        : this(name, internationalName, code, isoCode2, isoCode3, timeZoneOffset, [currency], dialCode, domain, ioc) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Country"/>.
    /// </summary>
    /// <param name="name">Название (на русском).</param>
    /// <param name="internationalName">Название (интернациональное).</param>
    /// <param name="code">Цифровой код страны.</param>
    /// <param name="isoCode2">Двухсимвольный код страны.</param>
    /// <param name="isoCode3">Трёхсимвольный код страны.</param>
    /// <param name="timeZoneOffset">Смещение часового пояса.</param>
    /// <param name="currency">Основная валюта страны.</param>
    /// <param name="dialCode">Международный телефонный код страны.</param>
    /// <param name="domain">Домен.</param>
    public Country(string name, string internationalName, ushort code, string isoCode2, string isoCode3, TimeSpan timeZoneOffset, Currency currency, ushort dialCode, string? domain)
        : this(name, internationalName, code, isoCode2, isoCode3, timeZoneOffset, [currency], dialCode, domain, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Country"/>.
    /// </summary>
    /// <param name="name">Название (на русском).</param>
    /// <param name="internationalName">Название (интернациональное).</param>
    /// <param name="code">Цифровой код страны.</param>
    /// <param name="isoCode2">Двухсимвольный код страны.</param>
    /// <param name="isoCode3">Трёхсимвольный код страны.</param>
    /// <param name="timeZoneOffset">Смещение часового пояса.</param>
    /// <param name="dialCode">Международный телефонный код страны.</param>
    /// <param name="domain">Домен.</param>
    /// <param name="ioc">Международный олимпийский код.</param>
    public Country(string name, string internationalName, ushort code, string isoCode2, string isoCode3, TimeSpan timeZoneOffset, ushort dialCode, string? domain, string? ioc)
        : this(name, internationalName, code, isoCode2, isoCode3, timeZoneOffset, [], dialCode, domain, ioc) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Country"/>.
    /// </summary>
    /// <param name="name">Название (на русском).</param>
    /// <param name="internationalName">Название (интернациональное).</param>
    /// <param name="code">Цифровой код страны.</param>
    /// <param name="isoCode2">Двухсимвольный код страны.</param>
    /// <param name="isoCode3">Трёхсимвольный код страны.</param>
    /// <param name="timeZoneOffset">Смещение часового пояса.</param>
    /// <param name="dialCode">Международный телефонный код страны.</param>
    /// <param name="domain">Домен.</param>
    public Country(string name, string internationalName, ushort code, string isoCode2, string isoCode3, TimeSpan timeZoneOffset, ushort dialCode, string? domain)
        : this(name, internationalName, code, isoCode2, isoCode3, timeZoneOffset, dialCode, domain, default) { }
    
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Country"/> из данных сериализации.
    /// </summary>
    /// <param name="info">Объект <see cref="SerializationInfo"/>, содержащий данные о сериализации.</param>
    /// <param name="context">Контекст потоковой передачи данных.</param>
    public Country([NotNull] SerializationInfo info, StreamingContext context)
        : this(
            info.GetString("Name") ?? string.Empty,
            info.GetString("InternationalName") ?? string.Empty,
            info.GetUInt16("Code"),
            info.GetString("IsoCode2") ?? string.Empty,
            info.GetString("IsoCode") ?? string.Empty,
            TimeSpan.FromHours(info.GetDouble("TimeZoneOffset")),
            info.GetUInt16("DialCode"),
            info.GetString("Domain"),
            info.GetString("IOC"))
    { }

    #endregion

    /// <inheritdoc />
    protected override async void OnPropertyChanged(string? propertyName = default)
    {
        base.OnPropertyChanged(propertyName);

        switch (propertyName)
        {
            case "Code": await CodeChanged.On(this).ConfigureAwait(false); break;
            case "IsoCode": await IsoCodeChanged.On(this).ConfigureAwait(false); break;
            case "IsoCode2": await IsoCode2Changed.On(this).ConfigureAwait(false); break;
        }
    }

    /// <summary>
    /// Возвращает хеш-код для объекта.
    /// </summary>
    /// <returns>Хеш-код для объекта.</returns>
    public override int GetHashCode() => IsoCode.GetHashCode(StringComparison.InvariantCultureIgnoreCase);

    /// <summary>
    /// Сравнивает текущий экземпляр <see cref="Country"/> с заданным объектом.
    /// </summary>
    /// <param name="obj">Объект для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов совпадают, иначе <c>false</c>.
    /// </returns>
    public override bool Equals(object? obj)
    {
        if (obj is null) return default;

        if (obj is string str)
        {
            if (str.Length is 2)
                return str.GetHashCode(StringComparison.InvariantCultureIgnoreCase) == IsoCode2.GetHashCode(StringComparison.InvariantCultureIgnoreCase);
            else if (str.Length is 3)
                return str.GetHashCode(StringComparison.InvariantCultureIgnoreCase) == GetHashCode();
            else
                return default;
        }

        if (obj is ushort code) return code.GetHashCode() == Code.GetHashCode();
        if (obj is Country country) return country.GetHashCode() == GetHashCode();

        return default;
    }

    /// <summary>
    /// Сравнивает текущий экземпляр <see cref="Country"/> с заданным экземпляром <see cref="Country"/>.
    /// </summary>
    /// <param name="other">Экземпляр <see cref="Country"/> для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов совпадают, иначе <c>false</c>.
    /// </returns>
    public bool Equals(Country? other) => Equals(other as object);

    /// <summary>
    /// Преобразует текущий экземпляр <see cref="Country"/> в трехсимвольный код страны.
    /// </summary>
    /// <returns>Трехсимвольный код страны.</returns>
    public override string ToString() => IsoCode;

    /// <summary>
    /// Преобразует текущий экземпляр <see cref="Country"/> в цифровой код страны.
    /// </summary>
    /// <returns>Цифровой код страны.</returns>
    public virtual ushort ToUInt16() => Code;

    /// <summary>
    /// Заполняет <see cref="SerializationInfo"/> данными о текущем объекте <see cref="Country"/>.
    /// </summary>
    /// <param name="info">Объект <see cref="SerializationInfo"/>, который наполняется данными о текущем объекте.</param>
    /// <param name="context">Контекст потоковой передачи данных.</param>
    public virtual void GetObjectData([NotNull] SerializationInfo info, StreamingContext context)
    {
        info.AddValue("Name", Name);
        info.AddValue("InternationalName", InternationalName);
        info.AddValue("Code", Code);
        info.AddValue("IsoCode2", IsoCode2);
        info.AddValue("IsoCode", IsoCode);
        info.AddValue("TimeZoneOffset", TimeZoneOffset.TotalHours);
        info.AddValue("DialCode", DialCode);
        info.AddValue("Domain", Domain);
        info.AddValue("IOC", IOC);
    }

    /// <summary>
    /// Возвращает экземпляр <see cref="Country"/> по его символьному коду.
    /// </summary>
    /// <param name="s">Символьный код страны.</param>
    /// <param name="provider">Параметры форматирования.</param>
    /// <param name="result">Экземпляр <see cref="Country"/>.</param>
    /// <returns><c>True</c>, если экземпляр был найден, иначе <c>false</c>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Country? result)
    {
        result = s?.Trim().ToUpperInvariant() switch
        {
            "AUS" => AUS,
            "AU" => AUS,
            "AUT" => AUT,
            "AT" => AUT,
            "AZE" => AZE,
            "AZ" => AZE,
            "ALA" => ALA,
            "AX" => ALA,
            "ALB" => ALB,
            "AL" => ALB,
            "DZA" => DZA,
            "DZ" => DZA,
            "VIR" => VIR,
            "VI" => VIR,
            "ASM" => ASM,
            "AS" => ASM,
            "AIA" => AIA,
            "AI" => AIA,
            "AGO" => AGO,
            "AO" => AGO,
            "AND" => AND,
            "AD" => AND,
            "ATA" => ATA,
            "AQ" => ATA,
            "ATG" => ATG,
            "AG" => ATG,
            "ARG" => ARG,
            "AR" => ARG,
            "ARM" => ARM,
            "AM" => ARM,
            "ABW" => ABW,
            "AW" => ABW,
            "AFG" => AFG,
            "AF" => AFG,
            "BHS" => BHS,
            "BS" => BHS,
            "BGD" => BGD,
            "BD" => BGD,
            "BRB" => BRB,
            "BB" => BRB,
            "BHR" => BHR,
            "BH" => BHR,
            "BLZ" => BLZ,
            "BZ" => BLZ,
            "BLR" => BLR,
            "BY" => BLR,
            "BEL" => BEL,
            "BE" => BEL,
            "BEN" => BEN,
            "BJ" => BEN,
            "BMU" => BMU,
            "BM" => BMU,
            "BGR" => BGR,
            "BG" => BGR,
            "BOL" => BOL,
            "BO" => BOL,
            "BES" => BES,
            "BQ" => BES,
            "BIH" => BIH,
            "BA" => BIH,
            "BWA" => BWA,
            "BW" => BWA,
            "BRA" => BRA,
            "BR" => BRA,
            "IOT" => IOT,
            "IO" => IOT,
            "VGB" => VGB,
            "VG" => VGB,
            "BRN" => BRN,
            "BN" => BRN,
            "BFA" => BFA,
            "BF" => BFA,
            "BDI" => BDI,
            "BI" => BDI,
            "BTN" => BTN,
            "BT" => BTN,
            "VUT" => VUT,
            "VU" => VUT,
            "VAT" => VAT,
            "VA" => VAT,
            "GBR" => GBR,
            "GB" => GBR,
            "HUN" => HUN,
            "HU" => HUN,
            "VEN" => VEN,
            "VE" => VEN,
            "UMI" => UMI,
            "UM" => UMI,
            "TLS" => TLS,
            "TL" => TLS,
            "VNM" => VNM,
            "VN" => VNM,
            "GAB" => GAB,
            "GA" => GAB,
            "HTI" => HTI,
            "HT" => HTI,
            "GUY" => GUY,
            "GY" => GUY,
            "GMB" => GMB,
            "GM" => GMB,
            "GHA" => GHA,
            "GH" => GHA,
            "GLP" => GLP,
            "GP" => GLP,
            "GTM" => GTM,
            "GT" => GTM,
            "GUF" => GUF,
            "GF" => GUF,
            "GIN" => GIN,
            "GN" => GIN,
            "GNB" => GNB,
            "GW" => GNB,
            "DEU" => DEU,
            "DE" => DEU,
            "GGY" => GGY,
            "GG" => GGY,
            "GIB" => GIB,
            "GI" => GIB,
            "HND" => HND,
            "HN" => HND,
            "HKG" => HKG,
            "HK" => HKG,
            "GRD" => GRD,
            "GD" => GRD,
            "GRL" => GRL,
            "GL" => GRL,
            "GRC" => GRC,
            "GR" => GRC,
            "GEO" => GEO,
            "GE" => GEO,
            "GUM" => GUM,
            "GU" => GUM,
            "DNK" => DNK,
            "DK" => DNK,
            "JEY" => JEY,
            "JE" => JEY,
            "DJI" => DJI,
            "DJ" => DJI,
            "DMA" => DMA,
            "DM" => DMA,
            "DOM" => DOM,
            "DO" => DOM,
            "COD" => COD,
            "CD" => COD,
            "EGY" => EGY,
            "EG" => EGY,
            "ZMB" => ZMB,
            "ZM" => ZMB,
            "ESH" => ESH,
            "EH" => ESH,
            "ZWE" => ZWE,
            "ZW" => ZWE,
            "ISR" => ISR,
            "IL" => ISR,
            "IND" => IND,
            "IN" => IND,
            "IDN" => IDN,
            "ID" => IDN,
            "JOR" => JOR,
            "JO" => JOR,
            "IRQ" => IRQ,
            "IQ" => IRQ,
            "IRN" => IRN,
            "IR" => IRN,
            "IRL" => IRL,
            "IE" => IRL,
            "ISL" => ISL,
            "IS" => ISL,
            "ESP" => ESP,
            "ES" => ESP,
            "ITA" => ITA,
            "IT" => ITA,
            "YEM" => YEM,
            "YE" => YEM,
            "CPV" => CPV,
            "CV" => CPV,
            "KAZ" => KAZ,
            "KZ" => KAZ,
            "CYM" => CYM,
            "KY" => CYM,
            "KHM" => KHM,
            "KH" => KHM,
            "CMR" => CMR,
            "CM" => CMR,
            "CAN" => CAN,
            "CA" => CAN,
            "QAT" => QAT,
            "QA" => QAT,
            "KEN" => KEN,
            "KE" => KEN,
            "CYP" => CYP,
            "CY" => CYP,
            "KGZ" => KGZ,
            "KG" => KGZ,
            "KIR" => KIR,
            "KI" => KIR,
            "TWN" => TWN,
            "TW" => TWN,
            "PRK" => PRK,
            "KP" => PRK,
            "CHN" => CHN,
            "CN" => CHN,
            "CCK" => CCK,
            "CC" => CCK,
            "COL" => COL,
            "CO" => COL,
            "COM" => COM,
            "KM" => COM,
            "CRI" => CRI,
            "CR" => CRI,
            "CIV" => CIV,
            "CI" => CIV,
            "CUB" => CUB,
            "CU" => CUB,
            "KWT" => KWT,
            "KW" => KWT,
            "CUW" => CUW,
            "CW" => CUW,
            "LAO" => LAO,
            "LA" => LAO,
            "LVA" => LVA,
            "LV" => LVA,
            "LSO" => LSO,
            "LS" => LSO,
            "LBR" => LBR,
            "LR" => LBR,
            "LBN" => LBN,
            "LB" => LBN,
            "LBY" => LBY,
            "LY" => LBY,
            "LTU" => LTU,
            "LT" => LTU,
            "LIE" => LIE,
            "LI" => LIE,
            "LUX" => LUX,
            "LU" => LUX,
            "MUS" => MUS,
            "MU" => MUS,
            "MRT" => MRT,
            "MR" => MRT,
            "MDG" => MDG,
            "MG" => MDG,
            "MYT" => MYT,
            "YT" => MYT,
            "MAC" => MAC,
            "MO" => MAC,
            "MKD" => MKD,
            "MK" => MKD,
            "MWI" => MWI,
            "MW" => MWI,
            "MYS" => MYS,
            "MY" => MYS,
            "MLI" => MLI,
            "ML" => MLI,
            "MDV" => MDV,
            "MV" => MDV,
            "MLT" => MLT,
            "MT" => MLT,
            "MAR" => MAR,
            "MA" => MAR,
            "MTQ" => MTQ,
            "MQ" => MTQ,
            "MHL" => MHL,
            "MH" => MHL,
            "MEX" => MEX,
            "MX" => MEX,
            "FSM" => FSM,
            "FM" => FSM,
            "MOZ" => MOZ,
            "MZ" => MOZ,
            "MDA" => MDA,
            "MD" => MDA,
            "MCO" => MCO,
            "MC" => MCO,
            "MNG" => MNG,
            "MN" => MNG,
            "MSR" => MSR,
            "MS" => MSR,
            "MMR" => MMR,
            "MM" => MMR,
            "NAM" => NAM,
            "NA" => NAM,
            "NRU" => NRU,
            "NR" => NRU,
            "NPL" => NPL,
            "NP" => NPL,
            "NER" => NER,
            "NE" => NER,
            "NGA" => NGA,
            "NG" => NGA,
            "NLD" => NLD,
            "NL" => NLD,
            "NIC" => NIC,
            "NI" => NIC,
            "NIU" => NIU,
            "NU" => NIU,
            "NZL" => NZL,
            "NZ" => NZL,
            "NCL" => NCL,
            "NC" => NCL,
            "NOR" => NOR,
            "NO" => NOR,
            "ARE" => ARE,
            "AE" => ARE,
            "OMN" => OMN,
            "OM" => OMN,
            "BVT" => BVT,
            "BV" => BVT,
            "IMN" => IMN,
            "IM" => IMN,
            "COK" => COK,
            "CK" => COK,
            "NFK" => NFK,
            "NF" => NFK,
            "CXR" => CXR,
            "CX" => CXR,
            "PCN" => PCN,
            "PN" => PCN,
            "SHN" => SHN,
            "SH" => SHN,
            "PAK" => PAK,
            "PK" => PAK,
            "PLW" => PLW,
            "PW" => PLW,
            "PSE" => PSE,
            "PS" => PSE,
            "PAN" => PAN,
            "PA" => PAN,
            "PNG" => PNG,
            "PG" => PNG,
            "PRY" => PRY,
            "PY" => PRY,
            "PER" => PER,
            "PE" => PER,
            "POL" => POL,
            "PL" => POL,
            "PRT" => PRT,
            "PT" => PRT,
            "PRI" => PRI,
            "PR" => PRI,
            "COG" => COG,
            "CG" => COG,
            "KOR" => KOR,
            "KR" => KOR,
            "REU" => REU,
            "RE" => REU,
            "RUS" => RUS,
            "RU" => RUS,
            "RWA" => RWA,
            "RW" => RWA,
            "ROU" => ROU,
            "RO" => ROU,
            "SLV" => SLV,
            "SV" => SLV,
            "WSM" => WSM,
            "WS" => WSM,
            "SMR" => SMR,
            "SM" => SMR,
            "STP" => STP,
            "ST" => STP,
            "SAU" => SAU,
            "SA" => SAU,
            "SWZ" => SWZ,
            "SZ" => SWZ,
            "MNP" => MNP,
            "MP" => MNP,
            "SYC" => SYC,
            "SC" => SYC,
            "BLM" => BLM,
            "BL" => BLM,
            "MAF" => MAF,
            "MF" => MAF,
            "SPM" => SPM,
            "PM" => SPM,
            "SEN" => SEN,
            "SN" => SEN,
            "VCT" => VCT,
            "VC" => VCT,
            "KNA" => KNA,
            "KN" => KNA,
            "LCA" => LCA,
            "LC" => LCA,
            "SRB" => SRB,
            "RS" => SRB,
            "SGP" => SGP,
            "SG" => SGP,
            "SXM" => SXM,
            "SX" => SXM,
            "SYR" => SYR,
            "SY" => SYR,
            "SVK" => SVK,
            "SK" => SVK,
            "SVN" => SVN,
            "SI" => SVN,
            "SLB" => SLB,
            "SB" => SLB,
            "SOM" => SOM,
            "SO" => SOM,
            "SDN" => SDN,
            "SD" => SDN,
            "SUR" => SUR,
            "SR" => SUR,
            "USA" => USA,
            "US" => USA,
            "SLE" => SLE,
            "SL" => SLE,
            "TJK" => TJK,
            "TJ" => TJK,
            "THA" => THA,
            "TH" => THA,
            "TZA" => TZA,
            "TZ" => TZA,
            "TCA" => TCA,
            "TC" => TCA,
            "TGO" => TGO,
            "TG" => TGO,
            "TKL" => TKL,
            "TK" => TKL,
            "TON" => TON,
            "TO" => TON,
            "TTO" => TTO,
            "TT" => TTO,
            "TUV" => TUV,
            "TV" => TUV,
            "TUN" => TUN,
            "TN" => TUN,
            "TKM" => TKM,
            "TM" => TKM,
            "TUR" => TUR,
            "TR" => TUR,
            "UGA" => UGA,
            "UG" => UGA,
            "UZB" => UZB,
            "UZ" => UZB,
            "UKR" => UKR,
            "UA" => UKR,
            "WLF" => WLF,
            "WF" => WLF,
            "URY" => URY,
            "UY" => URY,
            "FRO" => FRO,
            "FO" => FRO,
            "FJI" => FJI,
            "FJ" => FJI,
            "PHL" => PHL,
            "PH" => PHL,
            "FIN" => FIN,
            "FI" => FIN,
            "FLK" => FLK,
            "FK" => FLK,
            "FRA" => FRA,
            "FR" => FRA,
            "PYF" => PYF,
            "PF" => PYF,
            "ATF" => ATF,
            "TF" => ATF,
            "HMD" => HMD,
            "HM" => HMD,
            "HRV" => HRV,
            "HR" => HRV,
            "CAF" => CAF,
            "CF" => CAF,
            "TCD" => TCD,
            "TD" => TCD,
            "MNE" => MNE,
            "ME" => MNE,
            "CZE" => CZE,
            "CZ" => CZE,
            "CHL" => CHL,
            "CL" => CHL,
            "CHE" => CHE,
            "CH" => CHE,
            "SWE" => SWE,
            "SE" => SWE,
            "SJM" => SJM,
            "SJ" => SJM,
            "LKA" => LKA,
            "LK" => LKA,
            "ECU" => ECU,
            "EC" => ECU,
            "GNQ" => GNQ,
            "GQ" => GNQ,
            "ERI" => ERI,
            "ER" => ERI,
            "EST" => EST,
            "EE" => EST,
            "ETH" => ETH,
            "ET" => ETH,
            "ZAF" => ZAF,
            "ZA" => ZAF,
            "SGS" => SGS,
            "GS" => SGS,
            "SSD" => SSD,
            "SS" => SSD,
            "JAM" => JAM,
            "JM" => JAM,
            "JPN" => JPN,
            "JP" => JPN,

            _ => default,
        };

        return result is not null;
    }

    /// <summary>
    /// Возвращает экземпляр <see cref="Country"/> по его символьному коду.
    /// </summary>
    /// <param name="s">Символьный код валюты.</param>
    /// <param name="result">Экземпляр <see cref="Country"/>.</param>
    /// <returns><c>True</c>, если экземпляр был найден, иначе <c>false</c>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out Country? result) => TryParse(s, default, out result);

    /// <summary>
    /// Возвращает экземпляр <see cref="Country"/> по его символьному коду. 
    /// </summary>
    /// <param name="s">Символьный код страны.</param>
    /// <param name="provider">Параметры форматирования.</param>
    /// <returns>Экземпляр <see cref="Country"/>.</returns>
    /// <exception cref="FormatException" />
    public static Country Parse(string s, IFormatProvider? provider) => !TryParse(s, provider, out var result) || result is null ? throw new FormatException() : result;

    /// <summary>
    /// Возвращает экземпляр <see cref="Country"/> по его символьному коду. 
    /// </summary>
    /// <param name="s">Символьный код страны.</param>
    /// <returns>Экземпляр <see cref="Country"/>.</returns>
    /// <exception cref="FormatException" />
    public static Country Parse(string s) => Parse(s, default);

    /// <summary>
    /// Возвращает экземпляр <see cref="Country"/> по его цифровому коду.
    /// </summary>
    /// <param name="code">Цифровой код страны.</param>
    /// <param name="country">Экземпляр <see cref="Country"/>.</param>
    /// <returns><c>True</c>, если экземпляр был найден, иначе <c>false</c>.</returns>
    public static bool TryParse(ushort code, [MaybeNullWhen(false)] out Country? country)
    {
        country = code switch
        {
            036 => AUS,
            040 => AUT,
            031 => AZE,
            248 => ALA,
            008 => ALB,
            012 => DZA,
            850 => VIR,
            016 => ASM,
            660 => AIA,
            024 => AGO,
            020 => AND,
            010 => ATA,
            028 => ATG,
            032 => ARG,
            051 => ARM,
            533 => ABW,
            004 => AFG,
            044 => BHS,
            050 => BGD,
            052 => BRB,
            048 => BHR,
            084 => BLZ,
            112 => BLR,
            056 => BEL,
            204 => BEN,
            060 => BMU,
            100 => BGR,
            068 => BOL,
            535 => BES,
            070 => BIH,
            072 => BWA,
            076 => BRA,
            086 => IOT,
            092 => VGB,
            096 => BRN,
            854 => BFA,
            108 => BDI,
            064 => BTN,
            548 => VUT,
            336 => VAT,
            826 => GBR,
            348 => HUN,
            862 => VEN,
            581 => UMI,
            626 => TLS,
            704 => VNM,
            266 => GAB,
            332 => HTI,
            328 => GUY,
            270 => GMB,
            288 => GHA,
            312 => GLP,
            320 => GTM,
            254 => GUF,
            324 => GIN,
            624 => GNB,
            276 => DEU,
            831 => GGY,
            292 => GIB,
            340 => HND,
            344 => HKG,
            308 => GRD,
            304 => GRL,
            300 => GRC,
            268 => GEO,
            316 => GUM,
            208 => DNK,
            832 => JEY,
            262 => DJI,
            212 => DMA,
            214 => DOM,
            180 => COD,
            818 => EGY,
            894 => ZMB,
            732 => ESH,
            716 => ZWE,
            376 => ISR,
            356 => IND,
            360 => IDN,
            400 => JOR,
            368 => IRQ,
            364 => IRN,
            372 => IRL,
            352 => ISL,
            724 => ESP,
            380 => ITA,
            887 => YEM,
            132 => CPV,
            398 => KAZ,
            136 => CYM,
            116 => KHM,
            120 => CMR,
            124 => CAN,
            634 => QAT,
            404 => KEN,
            196 => CYP,
            417 => KGZ,
            296 => KIR,
            158 => TWN,
            408 => PRK,
            156 => CHN,
            166 => CCK,
            170 => COL,
            174 => COM,
            188 => CRI,
            384 => CIV,
            192 => CUB,
            414 => KWT,
            531 => CUW,
            418 => LAO,
            428 => LVA,
            426 => LSO,
            430 => LBR,
            422 => LBN,
            434 => LBY,
            440 => LTU,
            438 => LIE,
            442 => LUX,
            480 => MUS,
            478 => MRT,
            450 => MDG,
            175 => MYT,
            446 => MAC,
            807 => MKD,
            454 => MWI,
            458 => MYS,
            466 => MLI,
            462 => MDV,
            470 => MLT,
            504 => MAR,
            474 => MTQ,
            584 => MHL,
            484 => MEX,
            583 => FSM,
            508 => MOZ,
            498 => MDA,
            492 => MCO,
            496 => MNG,
            500 => MSR,
            104 => MMR,
            516 => NAM,
            520 => NRU,
            524 => NPL,
            562 => NER,
            566 => NGA,
            528 => NLD,
            558 => NIC,
            570 => NIU,
            554 => NZL,
            540 => NCL,
            578 => NOR,
            784 => ARE,
            512 => OMN,
            074 => BVT,
            833 => IMN,
            184 => COK,
            574 => NFK,
            162 => CXR,
            612 => PCN,
            654 => SHN,
            586 => PAK,
            585 => PLW,
            275 => PSE,
            591 => PAN,
            598 => PNG,
            600 => PRY,
            604 => PER,
            616 => POL,
            620 => PRT,
            630 => PRI,
            178 => COG,
            410 => KOR,
            638 => REU,
            643 => RUS,
            646 => RWA,
            642 => ROU,
            222 => SLV,
            882 => WSM,
            674 => SMR,
            678 => STP,
            682 => SAU,
            748 => SWZ,
            580 => MNP,
            690 => SYC,
            652 => BLM,
            663 => MAF,
            666 => SPM,
            686 => SEN,
            670 => VCT,
            659 => KNA,
            662 => LCA,
            688 => SRB,
            702 => SGP,
            534 => SXM,
            760 => SYR,
            703 => SVK,
            705 => SVN,
            090 => SLB,
            706 => SOM,
            729 => SDN,
            740 => SUR,
            840 => USA,
            694 => SLE,
            762 => TJK,
            764 => THA,
            834 => TZA,
            796 => TCA,
            768 => TGO,
            772 => TKL,
            776 => TON,
            780 => TTO,
            798 => TUV,
            788 => TUN,
            795 => TKM,
            792 => TUR,
            800 => UGA,
            860 => UZB,
            804 => UKR,
            876 => WLF,
            858 => URY,
            234 => FRO,
            242 => FJI,
            608 => PHL,
            246 => FIN,
            238 => FLK,
            250 => FRA,
            258 => PYF,
            260 => ATF,
            334 => HMD,
            191 => HRV,
            140 => CAF,
            148 => TCD,
            499 => MNE,
            203 => CZE,
            152 => CHL,
            756 => CHE,
            752 => SWE,
            744 => SJM,
            144 => LKA,
            218 => ECU,
            226 => GNQ,
            232 => ERI,
            233 => EST,
            231 => ETH,
            710 => ZAF,
            239 => SGS,
            728 => SSD,
            388 => JAM,
            392 => JPN,

            _ => default,
        };

        return country is not null;
    }

    /// <summary>
    /// Возвращает экземпляр <see cref="Currency"/> по его цифровому коду. 
    /// </summary>
    /// <param name="code">Цифровой код страны.</param>
    /// <returns>Экземпляр <see cref="Country"/>.</returns>
    /// <exception cref="FormatException" />
    public static Country Parse(ushort code) => !TryParse(code, out var result) || result is null ? throw new FormatException() : result;

    /// <summary>
    /// Приводит цифровой код страны к типу <see cref="Country"/>.
    /// </summary>
    /// <param name="code">Цифровой код страны.</param>
    /// <returns>Экземпляр <see cref="Country"/>.</returns>
    /// <exception cref="FormatException" />
    public static Country FromUInt16(ushort code) => Parse(code);

    /// <summary>
    /// Приводит символьный код страны к типу <see cref="Country"/>.
    /// </summary>
    /// <param name="isoCode">Символьный код страны.</param>
    /// <returns>Экземпляр <see cref="Country"/>.</returns>
    /// <exception cref="FormatException" />
    public static Country FromString(string isoCode) => Parse(isoCode);

    /// <summary>
    /// Приводит символьный код страны к типу <see cref="Country"/>.
    /// </summary>
    /// <param name="isoCode">Символьный код страны.</param>
    public static explicit operator Country(string isoCode) => FromString(isoCode);

    /// <summary>
    /// Приводит цифровой код страны к типу <see cref="Country"/>.
    /// </summary>
    /// <param name="code">Цифровой код страны.</param>
    public static explicit operator Country(ushort code) => FromUInt16(code);

    /// <summary>
    /// Неявно преобразует экземпляр <see cref="Country"/> в символьный код страны.
    /// </summary>
    /// <param name="country">Экземпляр <see cref="Country"/>.</param>
    public static implicit operator string(Country? country) => country is not null ? country.ToString() : string.Empty;

    /// <summary>
    /// Неявно преобразует экземпляр <see cref="Country"/> в цифровой код страны.
    /// </summary>
    /// <param name="country">Экземпляр <see cref="Country"/>.</param>
    public static implicit operator ushort(Country? country) => country is not null ? country.ToUInt16() : default;

    /// <summary>
    /// Сравнивает экземпляр <see cref="Country"/> с заданной строкой.
    /// </summary>
    /// <param name="country">Экземпляр <see cref="Country"/>.</param>
    /// <param name="str">Строка для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов совпадают, иначе <c>false</c>.
    /// </returns>
    public static bool operator ==(Country? country, string? str)
    {
        if (country is null && str is null) return true;
        if (country is null || str is null) return false;
        return country.Equals(str);
    }

    /// <summary>
    /// Сравнивает экземпляр <see cref="Country"/> с заданной строкой.
    /// </summary>
    /// <param name="country">Экземпляр <see cref="Country"/>.</param>
    /// <param name="str">Строка для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов не совпадают, иначе <c>false</c>.
    /// </returns>
    public static bool operator !=(Country? country, string? str) => !(country == str);
}