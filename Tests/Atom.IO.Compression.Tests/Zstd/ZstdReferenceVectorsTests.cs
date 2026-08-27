using System.Security.Cryptography;

namespace Atom.IO.Compression.Tests.Zstd;

/// <summary>
/// Сверка декодера Zstd с эталонными образцами, созданными настоящим zstd CLI v1.5.7.
/// </summary>
/// <remarks>
/// <para>
/// Зачем этот набор. Остальные тесты модуля прогоняют СВОЙ кодировщик через СВОЙ декодер,
/// то есть проверяют согласованность двух своих реализаций, а не совместимость с форматом.
/// Хуже того, собственный кодировщик вообще не выпускает сжатые блоки — только RAW и RLE,
/// поэтому круговой прогон не задевал ни префиксные коды литералов, ни FSE-таблицы,
/// ни обратные битовые потоки. Именно поэтому декодер годами не умел читать чужие потоки:
/// он разбирал заголовок сжатых литералов по неверной раскладке бит, считал длину описания
/// FSE-таблицы по позиции предзагрузки битового буфера, читал обратные потоки в неверном
/// направлении и строил дерево литералов каноном DEFLATE вместо канона Zstd.
/// </para>
/// <para>
/// Образцы лежат прямо в коде (Base64) вместе с длиной и SHA-256 исходника, чтобы тест
/// не зависел от внешних файлов. Набор подобран по покрытию возможностей формата:
/// RAW-, RLE- и сжатые блоки; все четыре Size_Format секции литералов; прямые и FSE-сжатые
/// веса Хаффмана; один и четыре потока литералов; Treeless- и RLE-литералы; все четыре режима
/// описания FSE-таблиц (предопределённый, RLE, сжатый, повторный); Number_Of_Sequences = 0;
/// кадры с контрольной суммой и без неё; кадры с известным и неизвестным размером содержимого;
/// несколько кадров подряд вперемешку с пропускаемыми.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ZstdReferenceVectorsTests
{
    /// <summary>
    /// Эталонный образец: сжатые данные и точный отпечаток ожидаемого исходника.
    /// </summary>
    /// <param name="Name">Имя образца.</param>
    /// <param name="Notes">Чем именно этот образец ценен.</param>
    /// <param name="PlainLength">Длина исходных данных в байтах.</param>
    /// <param name="PlainSha256">SHA-256 исходных данных (шестнадцатеричная запись).</param>
    /// <param name="CompressedBase64">Кадр zstd в Base64.</param>
    /// <param name="PlainBase64">Исходные данные в Base64 (только для компактных образцов).</param>
    private sealed record ReferenceVector(
        string Name,
        string Notes,
        int PlainLength,
        string PlainSha256,
        string CompressedBase64,
        string? PlainBase64);

    private static readonly ReferenceVector[] vectors =
    [
        new(
            Name: "ПустойКадр",
            Notes: "zstd -3: пустой кадр",
            PlainLength: 0,
            PlainSha256: "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            CompressedBase64: """
        KLUv/SQAAQAAmenYUQ==
        """,
            PlainBase64: """
        
        """),

        new(
            Name: "ОдинБайт",
            Notes: "zstd -3: один байт",
            PlainLength: 1,
            PlainSha256: "BBEEBD879E1DFF6918546DC0C179FDDE505F2A21591C9A9C96E36B054EC5AF83",
            CompressedBase64: """
        KLUv/SQBCQAAWgtXNV8=
        """,
            PlainBase64: """
        Wg==
        """),

        new(
            Name: "СлучайныеДанные",
            Notes: "zstd -3: несжимаемые данные, RAW-блок",
            PlainLength: 900,
            PlainSha256: "90D5AA57A9C8A8D37AF8B2A0BD7EAC02C37DD07A7C1E97930A1B81AFC51D87B4",
            CompressedBase64: """
        KLUv/WSEAiEcAHk82qSy8ZYCxzQLGo+mhGAH6q8iNnaUumTiK5JK/nuHdIWAu/NQDfyA2X8we9OEdN83QFvZXw2jXOWos81A2dv4VzqePcjgEabGj+T9
        1xDprt/cxfFbiFh2vkOyXSZ9Y6GRzypYBP0An67M/jyYyGceZeyBLSCm3eK0/uDToGicPmSN0B7mZAkmfvQuTw4fq4kjNw6SSdf0QeVrXrY+QFmD9/pF
        XhLVG9bw2aR15rfya5PnH8Vnikp4euq4EEN4onMeQNutouR9vRfb3iWmrheSM9XcAWDWJno0ZAZB3t6+EI4BZe742mzdZttTchq6xHlfgZBhxvhYUjDU
        4x2ZdCniO3xcmd95cMU7Kq9/KYmi0pj8oEue1C3wPnQKCtrjn5zkQbw1ZnHYW/Zqy7Mu98wlmGjTc2JTYXtmXGGLdFQl7qp9oiyCj1PbXDUpIZBSvwGL
        o+amBLBEYFLsXK4/7oKZCnQvSRaD/vExg8+dtIb9rL5W1AMYnaK2D0rdcdRg1uReVRPBShJM77DPnAvSX/cbsWe5ClfJ+iHlqjydvWUXtz6BNC3Z6Mxo
        wuoCBukBdxQZJLszPEUNviOOnkfHrqrfsSx+0uwiOUmKxKpfotLpLVp6nLykSuTD/Xs/e1r6s09z/f8akqYLMPE6oBEWLdBhWogF3VvTyqtca0p5TH3D
        vd1HcCiw6H0NLIZzJpURBFGxYbsQhztGrQ656VluP+QB/QZIJyyz1nQRi8M4B163t9pQ4zpMC1Rrs4JLBNMTfBmm8K3XSLyWWjjujoySyhl8EkFuRwMu
        S3yt7G4/KShVTtPHwWWbh06H0IcY5elGgF0JcQTpCSg151KN/oWJ6V4cy/sq9uiJfN/GhVtlBuKV+8Uu/TaoMRKGn0y7gVD6DleQNL2tOLlCsJM4h1zG
        qmj1cJdIFehgyRJpE81QtakhgWGduz8Vm9F8w9eHlqBIpv5rHL6lCVsG6rCMipwTzxDu0yrGKDxWV066RE3KTHMHN+h5CoiyabzduytZXvMBD2cpWdpT
        bszkJeHPDUTiS3bf53LNd6IiO1yIz2IzaBJkqzwGf8lcuPT9hTplTRuZehmw2Q6ntPLb+8BnFgRBt0zlLQimvb/klYQvz9UcvuQLaFJVpVYZPUAZF150
        Zdkdi5mnGzMSUfAiDWe59xRwfcT2/DnGnCTbQ/JDYHIRpEU2hFLVPKdgvkY=
        """,
            PlainBase64: """
        eTzapLLxlgLHNAsaj6aEYAfqryI2dpS6ZOIrkkr+e4d0hYC781AN/IDZfzB704R03zdAW9lfDaNc5aizzUDZ2/hXOp49yOARpsaP5P3XEOmu39zF8VuI
        WHa+Q7JdJn1joZHPKlgE/QCfrsz+PJjIZx5l7IEtIKbd4rT+4NOgaJw+ZI3QHuZkCSZ+9C5PDh+riSM3DpJJ1/RB5Wtetj5AWYP3+kVeEtUb1vDZpHXm
        t/Jrk+cfxWeKSnh66rgQQ3iicx5A262i5H29F9veJaauF5Iz1dwBYNYmejRkBkHe3r4QjgFl7vjabN1m21NyGrrEeV+BkGHG+FhSMNTjHZl0KeI7fFyZ
        33lwxTsqr38piaLSmPygS57ULfA+dAoK2uOfnORBvDVmcdhb9mrLsy73zCWYaNNzYlNhe2ZcYYt0VCXuqn2iLIKPU9tcNSkhkFK/AYuj5qYEsERgUuxc
        rj/ugpkKdC9JFoP+8TGDz520hv2svlbUAxidorYPSt1x1GDW5F5VE8FKEkzvsM+cC9Jf9xuxZ7kKV8n6IeWqPJ29ZRe3PoE0LdnozGjC6gIG6QF3FBkk
        uzM8RQ2+I46eR8euqt+xLH7S7CI5SYrEql+i0uktWnqcvKRK5MP9ez97WvqzT3P9/xqSpgsw8TqgERYt0GFaiAXdW9PKq1xrSnlMfcO93UdwKLDofQ0s
        hnMmlREEUbFhuxCHO0atDrnpWW4/5AH9BkgnLLPWdBGLwzgHXre32lDjOkwLVGuzgksE0xN8GabwrddIvJZaOO6OjJLKGXwSQW5HAy5LfK3sbj8pKFVO
        08fBZZuHTofQhxjl6UaAXQlxBOkJKDXnUo3+hYnpXhzL+yr26Il838aFW2UG4pX7xS79NqgxEoafTLuBUPoOV5A0va04uUKwkziHXMaqaPVwl0gV6GDJ
        EmkTzVC1qSGBYZ27PxWb0XzD14eWoEim/mscvqUJWwbqsIyKnBPPEO7TKsYoPFZXTrpETcpMcwc36HkKiLJpvN27K1le8wEPZylZ2lNuzOQl4c8NROJL
        dt/ncs13oiI7XIjPYjNoEmSrPAZ/yVy49P2FOmVNG5l6GbDZDqe08tv7wGcWBEG3TOUtCKa9v+SVhC/P1Ry+5AtoUlWlVhk9QBkXXnRl2R2LmacbMxJR
        8CINZ7n3FHB9xPb8OcacJNtD8kNgchGkRTaEUtU8
        """),

        new(
            Name: "ТекстУровень19",
            Notes: "zstd -19: FSE-сжатые веса Хаффмана, один поток литералов (Size_Format 0), FSE-таблицы последовательностей",
            PlainLength: 9000,
            PlainSha256: "C0502413189D0A0C452E257EE56578CEB479089487013B4BA038F7674BA17C18",
            CompressedBase64: """
        KLUv/WQoIt07AKLNHxOwF9rHJzXK7rWcDcm2TpLoyAM6bVtnayxxbos1GnvrbW7Gc/sSrbN92ank6XVD5+ir575or56dNTrFbW43m5ezLUPU2HL2la3x
        VjcozGuUBkhoTkU4x1hCJMIYh0pgw9gRG/BTuwLAdWNQ5lUqZARKVSQY51BAFJGhMg5D5RyDtagSL5qUVLLHASIQCAgGxhNFlLUPEgAhgIRRNGnIIKWY
        vQNp59/yTTVb2MGd7978rlY51IfnQTsMRKE4QrwQtaqxD4KvK/B+c6F9/iyN18wB4DHZdnD9tIr/hGLYk5RkXDEByVsuiI7v9kRly1Jfi2h/QAUHBJIe
        5w3owBFxvdEo+yCAi/+Jv+MtOEkyaFBqP2ZBCLI0blo65KKiCciLKOWLFmfUm6Qfjg9xOe1701h+G7Bau5oNyDsQg7in0+cTqMm0brMsULNkUr/ntrWZ
        l1I1JIujNc9RCcTFSO8LcONljfWphnekQMNj6abM/Olceu5E0BYOWzpFVGo848VQjUwhZuosBJUgHLaeaLFQ/WA0eCPyX8rrMxsfpnw0tEzfMw5rE4pW
        vMqTkvZzB8TG+oBov/eUE5kuzf4TXtB4TX9j7J1jby7qNCzbfc7txkXXtgyqGKIqm5qPz24iQ8VI9j8+GlaxTaq4fCtAUJjHiPsRbaB9XiVBgJ6+No3w
        8klcHIQv6YuvhxYF+YnAHPT7cy9FB0Mf6rsaco3vznBSTzwyvZyzTlFeRVliN7s2RNtp9yMy86rbni0u67BdR+6qZg8ge0ZHo6Y5f9kRk8rIyGG84t5p
        EPkoJYiPYByUPGugRuHBFrO3nqyJ/TDN2n1e291HoBJIW6mhJsFiGLrFDrQQwQWfdyDbqxs9iPrryY6IEnt2kxi9oB2pJwgdTrsP8ToQOB6fjFhc6P4L
        +96XXAXsUssJq56rWCTVgICMr6k23OvCK3CbP8ElCImy9NMVixjPGmFSZH5qPxxjUGnPE4LHsECpOTjsegsNoBF9lfjk7xyEiNgaCmeC+GumS1NiNEoU
        8x0gRXencpxQJwiDw8B/2U33v5ZSzaUPjdzoz6nXWALvyDhCiuDmiDlDZPEh2Y2H5OsHmEDrBKR8nJhY2NbBizO6KGnT5ViW4HN1KPfe4kM9TOeqsFcW
        cto7864x0LmEIpL9lP+zzKZNaeQj8/2icLs5MvZ2ff6AsBwZiHinxqKs2cARACRO5Nbf5rQxw8Zp3k0KQU03JGZJhcIrhU2eMZGiMWR9Ydhde+tI9Xnl
        9ga8OdzTiqun5aCK2RWyF4Zwm14M8clhr7/uqISdA5ih2M+4nU8DkTLnL+YE2plaK/CiwqeFy2jpzJppWIjSD/V/Cs7YzQtmw6UgBrIpvbvE1SZHAKbQ
        CCg7LyYfkq1niK5+0fJbCxpnU6Fdd0cfaLVxA88F+ScvMLI0F0EuOUKKL4ndJ2t6TFflr/yeYTYc9WBzczL7BXqRrYV32CmHdzsFBagsuhfgeKYDm0ip
        i6yxInHUhzhUSJUM1wkTUEwJWvIYlbnIxo++EMXXy5GwNbYDVzVbUEXgwVQ4nVykrPTWSPQXpuCXvshTQ7bfp/oeZ6dADtXDwRVykwtO3jFbflNspoQx
        ZeoONEslW4h6JsXNjaCMeuMW0URfWKxEtsOO3gzasE4Izv+A4CAAHOiCQso9iI9PHngKNnvSgi9vCjEyw7R3cwr3cAZMp7nA06PsBDiJsTfLEdCfsgNU
        K0JjcQIaBUblVQMmSrcnoxXhl5WQ32cMoGumQatZ+krGI9scHEco7aPMGAZoOpvxNWQnbehL+6SHxxRKo9GWloZTZfxclRmxExFi20BU2YAebVOQorQF
        DhI4tWg6UYCmDylSud3iqOdS+xIRQArsPz8pywT2ttgDXuiG0QRJHRoSSLNtvjkG0T1CiyW6JkFvXWiSOE5G7Itk/PSw6x30v0FRQUJRSEk1cBoNiBCN
        pXiV5ccojLJYbuxx4BgYTlvi8aOfNtICY1lGV5m3yCo0Y5DHegBYyMSM4vt6GIi/AwW+XF9bCHBOKcspqBlB83SPutx4R2IpDB1ZT7DDUg9wyAVDWP5F
        kIiYRJ/99gZswJugNG47J2UsMKKetvgPhl3WBrANbzDxqfUIbY7bopiH4fgYkziNKb2ijfVjSolECWh7svKQCJuvR0QUrVaJgThnWBdw3GCscqtgq0Hk
        K//19uPtK+5XZH6REYaIiy7fGOl9Iq0pDceYIHOIJnhsUZn2J87h9BZnL4xFXNNE9EJ4yQCPCISHweCpTBI5gIF/FDmqwrbIZdCAJv92H3gg+pA0sTVD
        l8Ozdy3cj14MjJ6kaAYDBwvlsY2s19RROkiS/xA74SFTw94RhQAENAUnC1ZaVdq/v+uwrEvbQCjB4jhTAhkbumePH1hDAHqALqsWz+xcoEtkH24RXXEA
        f5R0a+Rz2p7EdehZGHOCTymlchigydUi0UQHg43AmA1sBqlUzMt9YShbLjLS4tov+ouXEO10tvDRaaRziYy/2Dwjv69iXcFXYStipSkwybhJDrbVqQYA
        wrfHPsIAzPustQFDlS0d
        """,
            PlainBase64: null),

        new(
            Name: "ТекстБезКонтрольнойСуммы",
            Notes: "zstd -3 --no-check: четыре потока литералов (Size_Format 1), кадр без контрольной суммы",
            PlainLength: 9000,
            PlainSha256: "C0502413189D0A0C452E257EE56578CEB479089487013B4BA038F7674BA17C18",
            CompressedBase64: """
        KLUv/WAoIk1GAOYSLRagJWkxEEjCxNKWn7d1pH38/+994x4CLAAnACIARlhemRVAllQXsAFhGWyQSmMRANiVQQzpXWYYQrHrrgJkSyWIq4HKjENligW5
        pa0YEW0VT9HW9jJDbCtWtF3LlKLtemQvRLCl4hUiIL1xkRyKtALi/rw3fovXdrT8kD/ct/yjt+JJvSNbHt29ovisuJKlki2eAhXJod7z0/3/rVAU22Ke
        55jI10hx7rLVOgJT8dyWSESc/4QGqHPt3ZQq7RlzEEBgkJjQgEKgFusHE0CHJROReb5JMpVqA1u+mRw+FQqVyKmruxtsXzIFCMV3LxEcusdh9wQIgQyN
        l6KxwxmCCrn1nkVYVB6bbfJqUd0H4K/zoPLBiqFfBMJO1qhpGOVejz5iiD6Z/EqJipbnX5pA57K0roU74VOhwTBQfyw1GmMfLXIt3jMdyuxEzsdDai7m
        9S2gyfW2KMeEMAsQA5nBTUvjXKzRKvL8/uaY1VEljsdGBJOJYk+HdeQUlrns0wbiWgHB+wLEkAtOE+Vburzdjnag8oIs3Io0/IzH2Qx27/WlQ/aC+HC5
        /B65HAVmyTY8iD7stcKvwazvCBl994jge1yiC+ymGeOIavRAH1Jkl5x8NQgCoWHIoXdBLoeNJNK00jnk19Fx+KcmdVtzh59XyVXNxx+Rf+LwUp5J/k5K
        s5keULGkDDJc4bUU2JdLJG4/Kx8gBgpi/Ws/u1MusukWD4m6YDa2HIxogrxT0lvZGhuWzQ2nIQqIbjewlkEhQwQWPZrj39/HORowieyGJnc7d/IhxA5a
        WhRStsI5iB/pR37jl1JhL37ikVtRI7Jw44PgzjylHlhDaPwlIrdI6kXFqyW/UsGWQqvtcAdniPcH+SA8kNxyKrY8jkyZDe/WUEEwtKToATLC1wyb9vsb
        UIujQGwPkz6ub82RSmamf/cFpSpgQPFv3KhFeQL/Fbs8ahLcmbbwDysrgVBWZxKyj3zOk6d6qwyU99aSVRxFM/NfrxDzFBM8HzDp9gjzaIi/JH28kPnh
        YDk3+EU7BEk8qE2j4H8cSOyC3OKDjD+csBEpQv9BptzTLYR2lnozBtl26pxyr/gNxaXOcBHMpHi5n/psuLAZfZGrYBcD9L3gaf0R7Q+JA7REgac8Ej14
        PfL9QP/0uxfG0k8qyFCcdqQL+fRs1Q4WHPUOeNSyaBfth4XU7lhccu3DSdF8ggd1b8X6DGm7tnlgg+JdhZcQWWqu7kIrQf2afDHjErjfsZYqH9SSBB8g
        xO6c44QIpiAIM89VlIO265F4LMV2EPxUaHDjKgKROT3w4nrn5QiyikCdAzlbaLlCx1BQUFwnBZMWgiyqYY2Ttxq8Xd7xobHBkpK8bhzwqRwNo5f+e2VO
        sE4t8TyAjUweRmf7WN05SZcM6LcSn/3UQTyW9ShkLfkPHpUW0m2Tx0r3rlwj7sWbiZ3fRQBwWvFZHFDFKbBPBI9MGyuvuDLMQ1VftktDeVYOHQqDmmZI
        VWA+jJSnQsNgvcIhWQ0BCiN61ACM5oD5iCvGBswcpCdeXJgW9c/VB7t8LrwHwJ/WisXIu9gyMKGS9xzPlexrUPIyt/4uWCK1nf/joieUi+AUWMageAY1
        XlvAdpY+aMFp6P4Jxv+fg8xrjNmKlLHB1+8xwj9gNYF1tdvZCZD7pArlCHREPiK22s3VqYqM3xFn/DGxKAcI+9f8wJUA4wzhn9b/R5aas2ZjeSGDlMt0
        dDr5/npeqwr7lXSzMKuo1Ilcjh3q7B0b1pfX+fXzHSPzuptXD4eLzCP2B4OnG3L3VfMQDGeNYEJm5OSiIPS4UDJEJa9MVoIO8j6V1Dm/McEr9YDwejbS
        rF2nqPDAodsfhYsYBy9TJsDEKIv5+2KkeAwTosUMaaew7wz0Bvv+quue8Ia6IX7WdcVx2PkHh/t3xB3iSPrNC2F6A74DLUp9leP2DS7BdJanFw6INil8
        DkKLdmE/jvzdCyAuoDsNkTghrPDsaAxB/t+6a+/EIue+7fYRlLeKupr9Na45EXu9QpyiKtPHSM6REmDqn9m6uuQ9WtIg5OwSu1p2NSBYEvlmkGiVM5pU
        E1IRZYxK3wc6xzToEJe21cFPAkHtvjEASyDvgwalS19f5ewEsvsxozKoF1BjggHCDpCrjQ8yxNaX/kyVEvpqIUvG7Xm20A1ImUb2AB3n16wg9gzrLOsb
        mKqYxXctgbnStL1MdoXQN2oaSA+8DQHi80J6MYGT2lmKmgAAIP0/6UoZJF10zmBU2h/A9wR/oXG/4gciKEsJ3SwUnyLdn7GfoqEsNKdON8LC454ewgAe
        pa5YFv3KGjBxJNEl3BqpgE/Rvwky5jjxTpBXvluh2v2tFFrIYTtKnrx04us/Yp/QkYKP90IsKBIVGj4mwzSA+XrmheJT5jhyomiGiep4qhWunltQKJF6
        t13YM4UDP8ecbzXt9DiVULoUHS8NNaDMBdhLnAVIixLIkMr/ZTBQgVj/MsjfAFsSx7u5BuoOTIdh31LjPzWwZ139u+zAd4oBqXFzHK0QAG0ZAQOKThFS
        MTWBVXxSxPvaMAElpYriKmMqW8MkEZfvVcl4TxVhTPiEVmVQiNcVy2Pfy7d48lXWATaU36s3aOIH92OtjzwpMsLQJaj0C8H0zv3kXNNOCgUbWB2qSicN
        ZsDL7eL9cTRMaTrOxyBU9m8WaQ2sLzzSAiMiDY8qrOVE9zRqGwRR09EP6i8qW3/Od9toSDQV7ARbcB+2Yl+OsxxcmDzse2gCKK8xgCMlVoj8JS3wo9kT
        a7SJ6wFnMD2W/OTHhA5K7imKLj+fMyjkl3xTdVc36wmad0tp68SpkpRoMiJUBWb3fyoxwAIAyxQkw3RckESGkp14pdJkbW92nnFmpOpcau4xyIBy1hv/
        XB+Qf9wQETPMaXwccoOVLncfpn3ruVXoSRkUxGRb2JNxabR7hwJfT/EOn4y06k3UIsZJs9dJYMRCU4mVLmnr15nrUrJMjTaZECXd7dCZuMizI+0K
        """,
            PlainBase64: null),

        new(
            Name: "ТекстПотоковыйКадр",
            Notes: "zstd -3 со stdin: размер содержимого неизвестен, окно берётся из Window_Descriptor",
            PlainLength: 9000,
            PlainSha256: "C0502413189D0A0C452E257EE56578CEB479089487013B4BA038F7674BA17C18",
            CompressedBase64: """
        KLUv/QRYNUYA1pxBFaClIA/w3rwIIV7EB81+qKpaP2gTAkAAOgA4AMknfMazSCqzgk18KAcjPCuzIlRL5SdzBKRSxgJqQA4HNZAsP1SAMhS0wvbZgRBg
        yaz4mE2AVMsIgs1AZsWQWayVLNW3IlewfSBp5fOV1eF3wvdZIWx8J2yVK0Ll+aqrPysCkr46nE8qXy08e54dvuqzzQok7SPpeVYqfST9Rf7Aszb7j7f9
        SHqrDu/Z6v8q5eNsgqQ323/lB9vs+UayGV3sPCvY5D/fB8+eb1+NpEgqMkICGcrBvMd/QdK/+r8fSa2PpHzh1ec7kuZvUplCLvT304YdSaf6SMozzoOk
        4SqTbxySIukrT8ikAoO+qAJ+VSNJQaG0cUIINCggGuiBsOoDEkDgcCzcEzF1leVb1ocajGI8dRXd2OtLQwEecvdgcM4eKyXcIIRq6L8EGHtuCHhk8wFw
        hOX/WMkkD4sMcQDW6wxaXw6l65dV2tEaHzSA3KvoQgzIJ1e7TtDG8jhGEyhY1ttaw/0hqosZBtrHrkYSu9Fy36F/RkB5OtEWxmAZc+YWeIkcWxRsQsCC
        2ECAuIMhI5eL0RR5einlWOVwiSPvRuTlFwgkGmKhl43spg1crafyyzILEOIFThbihnS83WnOJJPvdrB6Pp8o2++5Elo8DiettT5ROlwgA14UmpDt8CBR
        4b5fc7JeEDIZPaAlWXnscN+aEYzoVw99yEo2rvNVrnaAhjiElqUyhoU1C/Ahv+P65Xzdk+FaxHnd3Ox89hFxJ2JBA53039nEbHYJKkhnhMPFRikobyuR
        r/2hA1HqGpS/9YrR2Ui5w2pTmFu1CdexH8EErds7/719EL/BGelGJyOai84N7N8GVX+Fx2JuDmyzO+xVB4yKjUyzMYzdInpBSektvbIX/GiDht9Yq1Tf
        r09c55ZTrKRwAMeKzjxSD1NDGj46IkskbUHk+wAK4SmxMrFgzkj6MxCQ4bsl/F43MUdGzGJWFKjgdwxR38UQbbqKN74sp4CpPQ59Lr5dkUpnngONKiiH
        cmoMzAAZZZbnxlLJ8iSS4McUmr8LCQiI1kooAf9IAyd/xTFVZnestcpqjqLOfKxXjxmNXe1OmWR/BOahjLS94CXNj8KS+PLrdohcPPykQfBdHaB1V27d
        QTY/fKIiaALyM5Fy/VtIdWaVCWOQ0bHnpXt9ftOFY9ffqgrtfuLZOML+9XWoAl7MKfeS03JHdJzQKIjmydOX0uv1FRBt5gSQggJULEFD/caF0NZFfeiC
        u++AQA2NStNeKaSwk+KyaH9KRczn7QQgVn/ItovJb37n4qErQRzk6WTEzGjIeC37L4/38pHf0Ury1mgCdNtddJzkYQaDYBgGhXI4yEwkdkqROTKeyDGw
        lpc8AInoPS+ed5px4BeBP+c7m125HscwmQh7vTGTXURySTXsOAGw2LfCG88cGy1JzMvgAE5V3DB+mXuPh5wYiTw4jwytYZit3OHFOT3ADy+vSN5PyrIq
        herVA0yMminSiJVtBR33xjihDCC9YR2fWZiw0iiqMKuG2OlJbgy9QgBLp7f41i5Nm2s52AMhUFM/YVZg9uZWKkIiWJ/C0oX7UNQMuw3gszmaacPfwDWH
        6Un4uHh9lUrBLiZX70EtTXctBlQXhYEoKpnPIX0tr2GSf9ymYnQihc8f4JImlOY9K+DFp9X8axG1W+k5y0xD0z9grOY5ZP7ImJfYq41mbVB1k1QQ3hKE
        Xg6HPqmgvIM+qOScrebqcAXFr4QzCiaHFAcE/8UbOBOg8fzwa/qNRl7NC2cTv3qPltt0lDkZ9ZimCvmqvs8woChug7N4c+rZJY031ONaB4CVzA/v5s0E
        gEeRDxSPY4jugycEPBggJzhHDhb1Vo9ok0EoY6B0JWDIi1XuuagaM16VBZmva8RIm+LDA6Zb0apFiXUA0DDZAIJRSubaMkOKmDA9NhiHIMoGvOPEDcb7
        7i4Y4fF1nzZkV0qtBOoLDlu+E9wjzfY7wgmjGZR4B0b7oLeXZt4+zNJ5AQAzJjXmIK84LJzhCO7eGWQfgKelBBDyCo9BeSAgqqdbgk5KcuiOjyJOfLIp
        LrOzcWwTFfErBBTVmcZFYQ6fALuxzNrVVeARmQYyJ9IiNyi+D8je2dgBXqvwA6aWNwoVtazGvMtYVpfyWcXwryBgvG8LzJWl+lxzIvOVMDxSNF8DRzD0
        F7TEqAa0nThX4z5uiDyXLzOFxIlS4DGgBykcydJH839A1dFUPMTaYXHLzwZwxa+XTUuLXGnatgEkUBqrByqbFuo4zfrDrJpeSo74BkT6v2qlhHcaBUyw
        SPtvoJOgdWggXsMDX9qqhGrWxoUicwEYxcvWOan6xoocV3xdQPAwqsTy1K+9ITASEuBVRDL8gP4P1Vi7Ea2WFoOMKwyjP6lkdQgsiTHvlp7x87+vCwMd
        GI+HhUXGQgvBAdMJAAHWnOKYWzsCeQYEb6Bci7gNt7ApfvXuufgz1yM0T0nL1s6MPclL19DB7ykHVJxuvDSj67GWBIghnL9mDDyBRL8wSMeAAnnMd70G
        P6uD5pZVk+qZy2SRAPHSE74RnLNVdjzRDVVU02lcAloEoldAku6rtbUNLYWIIkSnJzsU7zL8uFwvOyKGUgQxUoUp7usei7DnPbQUvko4wI3SeO0ALe3/
        3OfIsSI7h9oC+JSnwOIw/OxO22wIdKCqmiRfc2k4iUZHesVNXejMnipJXzDkwlMD2EhqeEKwzcnTU7/aBIhij8wga1myn3H176aGc722cF8cr1yvvYO5
        Z11rAhYgvMXkL2mdId5UdvLNrV7jVKVDQ9JUh59S84PILe0MK3mV51QqXY2mUVgaoE5nYXAJhvmGtqIZYoBoIF52IVBm63FZQjb5TsjIgp/RZcw5EFAZ
        afOIQzDAyLbpaAPvhONFRmAUA/pcN/7MpPti2XD77fiJXbXIJuuTVGs7bYejIyeJSD1hReZVi6Ub4lDB8c++Q1wlUm3Y+lsgKo6rSgPXMK1DlS0d
        """,
            PlainBase64: null),

        new(
            Name: "ДваКадраИПропускаемые",
            Notes: "два кадра zstd подряд, разделённые пропускаемыми кадрами",
            PlainLength: 18000,
            PlainSha256: "42FDC402C4BD0EBCD89E685AA7C2D4CE22BCEBB3CDCC6291BC3ECC57B8E0F23E",
            CompressedBase64: """
        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
        """,
            PlainBase64: null),

        new(
            Name: "МалыйАлфавит",
            Notes: "zstd -19, алфавит из двух байт: прямые 4-битные веса Хаффмана и Size_Format 3 (18-битные размеры)",
            PlainLength: 20000,
            PlainSha256: "0C2FFD539F029ECD16692945CAE24560A9C25487229E0A544774395E33A983CB",
            CompressedBase64: """
        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
        """,
            PlainBase64: null),

        new(
            Name: "ПериодТри",
            Notes: "zstd -3, xyz повторённый 50000 раз: многоблочный кадр и длинные коды ML 50 и 52",
            PlainLength: 150000,
            PlainSha256: "EBC74B9A2D04F8EEFC51465AD5D6B97121C5EAC0956D392E2EF7DB6BF0217276",
            CompressedBase64: """
        KLUv/aTwSQIAXAAAGHh5egEA+v/mbghFAAAIegEA7MkOhCIVmeY=
        """,
            PlainBase64: null),

        new(
            Name: "Нули",
            Notes: "zstd -3, 200 КБ нулей: RLE-блок рядом со сжатым",
            PlainLength: 200000,
            PlainSha256: "4CBBD9BE0CBA685835755F827758705DB5A413C5494C34262CD25946A73E7582",
            CompressedBase64: """
        KLUv/aRADQMAVAAAEAAAAQD7/znAAgNqCADE6XRw
        """,
            PlainBase64: null),

        new(
            Name: "СсылкаВОкно",
            Notes: "zstd -3: совпадения, уходящие в историю за границу блока",
            PlainLength: 6016,
            PlainSha256: "1E2789E830A926559C9D72C5077A095272ACAF2BEB2F9B59D5F021E1FA19B48A",
            CompressedBase64: """
        KLUv/WSAFk0/AJR9gHMn73Qb32wP4ix5OFABEQXNwHES3DE63jKZswiUnJlUMjS+8/twESj3ckE6MWXa40Xhvfmc+JLV8JpxGz80sHnbd/N7iulvYDW/
        VKHjvKUMglhbsX3mFA7ONUxLfF25LXa5ppvAVdQNEmTgbJIhd2oFnNGdedvZR4ylfvmj8BO3ByvE1Eyl7khpux9vbRvAd0varUT9rBiUII31snBBEZBm
        YV1wPDrrBl99VtuUPj0J08h67nl1TFMOQsFK8KOnf6w6J7c/lTvgexDoXIcSTSTrRaI3OyqqLlw5YEP8mr95iHw8b/7X5Jh0fRC2f7oDdK1hLxtMQkHn
        I1pUxenBkVkQYEz3TuJA3grWJzKrplxE9IoW6He4Hrss6q3h60pndiyy6OWn0pMoKbj69DJk8nomunImnAaBGgjnrIFQW/lyPhOI6MFGTz/rCaXbMU0g
        Kwe06KkSDpcnGJqq1sNU7O4BFZmuYo/v+H5zcNMsZHf/s1SqtMB6PjZQrOOf3D6xqLrBDZ+RIrdwA7ZmsevdRzlchEj0cbdnmwN/Rd7A7hkwzTkGA9po
        iHH819trVKF0uizqbDsxX+8CTX5lhA6uKOuFHXY7FCtRdHAvGv7ETNjoswPtbNAXimDGObMo+zZ38P/+fmbtG2fNf7ZM775FEy82Qh+s8KsCN5ykD94i
        lUfM1L7zt7cCG+ZcKxe6zEXWW6Y0nznyKZqj26jSH6kWk7rp8DLbe6eK6LIP4WYxtMxt1af9NQWXVc6Kn+7LrtWoLpOd/yO9teFLnHdhbapThf/MwFuk
        T1oLheZS4C8iJE4Lk0LGIcjvGJKV1N3v2ATcApOnBukx5GM5HCAhdY7sT/pZ5NN3WiJ6aDxr7be34p5HoG2lWIN9lnn47Sk4nTdWlGXZfg9VWb6gIOL9
        8C1yw0MLOTYslyARkIXVp7NUmMKvgEBu5CULGhjXYDswhCHmQRlqJ0gOIGbXgxZsm48UuRGK33HI0AH7Wv7eB5VqklnDCt2dSxu30qOfutO7L2Q2wNpX
        Fne9zxKQI0FQZkCznLSIohc1xVZlPp54Rye+0SAmOoOftQiu3QV4Uh4msm/PegUR6QPUkynkrR+gLkNmZvCZ8APMjDMXH7iWy5o51XeI4M0+eo1GUa4D
        25CS6njsTfY+FhLLmwLfIAzPM2GQeG0Uiih83olWLuOb7Jlix2GqHNbBE+9BIAiGB8tAEgfBoIdM5QMd/1HSx1w/WwFf4ZnlbW5LEmKmdQuhMbJGTss8
        idvHYWKiTEtPn428qguow/W5hoJfwVvJj7qJfpr41UXZZY8FhbCCAj4KvrMSHYlhPAuX1iciFihpHxJZZeHAnmKYLsBMzCFqz+rsO6AoPfvpWaNY60nx
        5bJvfHSjrxNcVv2cY0N/KE25unkI2fiA1Hrd7f1wopNCUJHQ3Bs9zmOFuul/JHS8xwlGr/FjfAwfc4fXlOhQxsBbmN52KtXq7jY4bbvbRd7OyUqXM9ne
        H/9IJTkzxDgZfoHEH7j+KX8FcZhalovIzSk+HYoU8cKh89Ynpjdxjda7oWNC0KMTy9fha6iN7FGlNCPFo2HuY6fi5VWIGWKpIrpdqvbXE3KPWsaShJ9a
        Pnt9AHT7EclQ+ULvkWeXSAYgegOp9uCEU1kwxWxFvKWTDmrXqJTYWHzmymAPwXvjYktKr0MZ1a16PELuIPVsAnyT33eE3AVpibpahgNjUjQDzp21d23R
        oh78jx16NhOOk2cNLbrR4rodqachBbw2IUnYWo7lodiVVGQmVAJQqx0nITusFWzq9nOb5S4YJbjBRU8wYZ6UDXTRLtfiPYoz4CnLjPghGAXQwhWv0RG4
        3BT3IdRhG8tdPMEIyJ9jPs9Rr0M0L82aBfRPO9be0W0AiudCY1Zm0uWFpVyxGHmldwId6Or3zTi7y7u0vecOKJUXrNpZGwe2PdO6H3OBLyZFcTaCzQeL
        6TQuqWLTAu26GiicEUHxudxAKEMdPZXgE41OvA/lkU/0Y6ubshZhZ/DszMvbY+1aFCY3SBLeCbrqnnN3fj2wWakWZAPMw3RNX3/Scv6CGHmtRYAaRajG
        zFLHoIL1A8x/GXIW5zvkR1UVlUiTeqiC4p3ori3xcXLrBM9/hgp6cQIt4q9jcXG9pv5uYc0F2V5b2/cwvXtFbs4wpwwckV1pL5AN3GnRvc3rM63OStRc
        MTYRbwntkSTjJYUSsM4++I4+BAjkuNSUWyKuLbXMODi1euVxAjGKt5jv/AvcKerXnpU+rL6rEltmBwLdQFaNENTD/kO8RFcro6eKY4J268yu7GxDX0CM
        WCa0UYmJvg4/Q4j+VDKrcjxZE5xanZrGAWcecM92a5m4io9+4ZNg9TaBlEEeAd6CY3qJ4oCHQ/4ZWccw1QmJYfgakijGvNnp6The57LK+CW7i0qfHPm6
        Lre+TvRsV9pF3ImFAiH02B8CUz19W+UiwOte3eVIbT1t4Zsolr53Lq1JwjNLYQgU94rtzYU2gzmWLIQhOf5iz8IdbldhAvDFMPLa3HnoCP7HE5AtRHHo
        hcPemT6IAtiFiYu79ccGzkzbdF+D1wlrePsJ0tZ3yFT0vgW7gciVMAWi7qdBM3BcDUNB/yjZO15Vr3GV2U/2sgNYj9mQaxTOh7/9G94KajzaW0y9IGEw
        FNPis+kvhIsWmAoAAAAAAAAAAAEDAM2DDzj3wr757f9MBflqXl0=
        """,
            PlainBase64: null),

        new(
            Name: "ЛогПовторныеТаблицы",
            Notes: "zstd -13, однородный лог: Repeat_Mode для всех трёх таблиц, RLE-таблица смещений, RLE-литералы и Treeless-литералы",
            PlainLength: 190518,
            PlainSha256: "0BB553916F098FC9EE9C96EBFCD185BBD6B17976143687DF375454122FCA66F0",
            CompressedBase64: """
        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
        HCd4p67KkQHFqXNyDPSaeh/HCZKpK3FkUF/qLBwDmaXeg+MEp9TVNzKgJnXujYE+Uu/dOEEgdcWNNDjwdtTbNrrQvUadayMSur+oNxt30BVFvWLjCrpF
        1PE1cqA7h/pY4wO6F+q7Gv/PHUIdUaP93BbU7zS2z61AfaUx+dz802E04p47fvpCo/Xc5dPvGXue257OndHx3MzTRzN+5x6e/pkx7dy20/Eyss6dOv0r
        I+ncndM3GX/OHTmdkNHLucPp+xjdcs6bPozxWc636bgYdeU8Nn0Toypnqul3GDfljDQdhZHI5o7N7V1zs+b2qrlRczvT3NLcTjR3aG7nmbszt9PMjZnb
        LXNT5vaSuSFze8fcjLmdYm5ibmeY6zg53CIAcogFBOAPwRL////////////////////4/4LgqKL+EgAXvMIQIPwBEvi/wZlrvwNq95R2D9o9nt1jdo9l
        95Ld89i9Yvccdi/YPX/d63XPXfdy3fPWvVr3nHUv1j1f3Wt1z1X3Ut3z1L1S9xx1L9Q9P93rdM9N9zLd89K9SvecdC/SPR/da3TPRfcS3fPQvUL3HHQv
        0D3/3Otzzz338tzzzr0695xzL84939xrc88199Lc88y9Mvcccy/MPb/c63LPLfey3PPKvSr3nHIvyj2f3GtyT8k968gdhZA7iB/3zh132DfuPBn3GS7u
        NivuKUzcQ424d3i4x9Rwl7Fwz5hwl3FwB0TBverAHWPAHa9/+479dsZ9+4x8e6Lu7RXq7Rnm7WKOt3d+tzNst4tYt0eVbhd5budRbp/nuJ1TuL3Bt71k
        tz2KbXuIbLsY1/bFQDqptqe2L6ftpO3waPvQ9v7ZjrM9bbbHbEeX7Svb8WT7yHb82L6xHS+2T2zHh+0L2/Fg+8B2/Ne+rx3vtc9rx3ft69rxXPu4dvzW
        vq0dr7VPa8dn7cva8Vj7sHb81b6rHW+1z2rHV+2r2vFU+6h2/NS+qR0vtU9qx0fti9rxUPugdvzTvqcd77TPacc37Wva8Uz7mHb80r6lHa+0T2nHJ+1L
        2vFI+5B2/NG+ox1vtM9oxxftK9rxRPuIdvzQvqEdL7RPaMcH7Qva8UD7gHb8z76fHe+zz2fH9+zr2fE8+3h2/M6+nR2vs09nx+fsy9nxOPtwdvzNvpsd
        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
        U7lQVqDswKWISpGTA7WMQ3cjo1o=
        """,
            PlainBase64: null),

        new(
            Name: "БезПоследовательностей",
            Notes: "zstd -13: блок только из литералов, Number_Of_Sequences = 0",
            PlainLength: 400,
            PlainSha256: "873A8C9DE26BAE11959DE3BF0ABE22926B69FB4F59CC690D232984B716913009",
            CompressedBase64: """
        KLUv/WSQAB0HAAbZNw6w5QEkSZIw0dRJkiTuBDMAMwAyALlbKYFzBWchi/hHAgAwkQSlvS6ppdxAGh4ODyB/v0ilq0eL42QzzOSbarW4wuwNmb12AyF8
        ++r0vE2Ufd8FhYosMgiisPmqj1EgVtn4XqihlVu9eEY+Colz3NO4VgnQ0HpVPsq7AXYrmXmV4QAQMegYK3na/7SBtQgEjsUpedOvDBGB0dfo3QMoY+mj
        pcgK/TjjIickiLZ5GjtkGtJ1SKsHWhh3R6kUlgAVMs9m2czmdwv0YlDBrhOI0z69EsQ/wVNFMPhZCd94MSgAtizH+Q==
        """,
            PlainBase64: """
        bW1nZmhoZWNpbWNwZ25nbG1nZW9ma2NjcGttYmlhbmdsY29nb2JsZWdmb2ZmYmRhZnBuY25jZWphbWtlbWdiZ29pbWVscGJlZW5kZGRlamZmbmVna3Bo
        aWRsZmNqYWJrZ21ta21tbmxjZmJsbW9oamhqZGtnaG1qb2NvbmljamtwZWluam5mam1vZ21rZ2xhbGpnZmluaG5sbWFkZ2FqZm9naW5vamdhZGpjYmJu
        amxqZ2RtbWZpb2tlZW9tbmZsaWdpb21mcHBmY2hvYWRrcGNrYWlha25pZmVibWJia2hqZm9ibmhnZHBmaWlsY2dmbGNkZWVlY2JtZmxoZmtia25namZk
        ZXBhaG1kaG9jZmZvbGlrYm1hamZlYWpqamRwbmNibGZra2tibWloZWFqbW5oZm5iZ2hpZmRuYWFnZWZqYmlmbmVhYm1vY2Zoa25lcGNsZmhwZ2RuZ2Zl
        cGhmaWZjaGhobm1jaGlra3BnZGFibmJrZ2JqZmlpYWpnbGRmbG1lamlnb2JhbGhlY21hbA==
        """),

        new(
            Name: "RleТаблицаДлинСовпадений",
            Notes: "zstd -1: RLE_Mode для таблицы длин совпадений",
            PlainLength: 400,
            PlainSha256: "B3C4726409D6F21AF39108825513AD9B943915B9EEE453668EE6A44077F18474",
            CompressedBase64: """
        KLUv/WSQAH0FABYYKAjg6XLvvRmg6iUAJQAkAKJGtYjHw59BTEUSdLiYgR5j8AI2uIUY9MAW1/JfzAZJDGHR4gLSgh4ErUpgQyFjXExlSPNoJsJBM2QZ
        yGjqa2Qvv42TJt7oAGMKk4poyJbRDvwbdIAmk+ymuRvIDEar5npJz3qZiQhwczHOgRyuWglGVqCIRApqKcavEyqU1XDG0YbJkTYkrBiExi0B9rTHWgED
        BAIWiMmCNoBIQAN5duW8
        """,
            PlainBase64: """
        aGFlYmdlYmNhZGFlZ2djYmhmZGhiYmJhZ2JnYmFlYmhjZmJnY2JkY2FiaGFjYmhjZGhiY2hkYWZiZ2NoZmRoYWRiZ2NhZ2dlYWFoZGNoZmJiYWRiYWZi
        ZGRoZ2FhZmdhZWdoZGFjZGJnZmhiZGZnZ2hhZmhoYmJlYmJoZWZnYmJhYWFoZ2ZlY2hlYmVkYWhnY2JhZGRmZmdmYmZnZ2RhZ2FhaGRoYWZkYWdkZ2Rh
        YmNlZ2FhYWJnY2FjYmJnY2FiZWZnYmJnZ2diYWRiZWNoYWhhZmRoYWhoYWNkZGRmY2ZiZ2NmZWJiZ2JmYmdnZ2JiZ2JhaGdhYWJnZmNoZmVjaGhhYmFo
        Z2hoYWJlZ2hlaGdjZGJnY2JoYmJiY2ZhYmdmZWJoZWRhYWVlYWFnaGdiZ2JkYmFiZ2hiaGNjYWdiaGFmZ2RjaGRnYmNhZ2RhYmdnZmFnaGNiaGdmYWZm
        YWRhYWFiZWFkZ2FkZmFhYmJiZGFlYWdiZ2FjYWFnZ2JiZ2dlZ2VlYWNnYWJlZmNnZ2FiZw==
        """),

        new(
            Name: "RleТаблицыДлинИСмещений",
            Notes: "zstd -19: RLE_Mode одновременно для таблицы длин литералов и таблицы смещений",
            PlainLength: 4080,
            PlainSha256: "1BCD33A509E07970E64AD9B0A0809ABE4E00D5ADC2D080B07499E7F4AB580BFE",
            CompressedBase64: """
        KLUv/WTwDmUAABhhYmEDUAEASs77ypVyzG8=
        """,
            PlainBase64: null),

    ];

    private static IEnumerable<TestCaseData> VectorCases
    {
        get
        {
            foreach (var vector in vectors)
            {
                yield return new TestCaseData(vector).SetName($"Эталон zstd: {vector.Name}");
            }
        }
    }

    private static IEnumerable<TestCaseData> ChunkedVectorCases
    {
        get
        {
            foreach (var vector in vectors)
            {
                foreach (var chunk in new[] { 1, 7, 4096 })
                {
                    yield return new TestCaseData(vector, chunk).SetName($"Эталон zstd по кускам: {vector.Name} ({chunk} Б)");
                }
            }
        }
    }

    [TestCaseSource(nameof(VectorCases))]
    public void ReferenceVectorTest(object boxed)
    {
        var vector = (ReferenceVector)boxed;
        var plain = Decompress(Convert.FromBase64String(vector.CompressedBase64), chunk: 0);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Has.Length.EqualTo(vector.PlainLength), $"{vector.Name}: неверная длина распакованных данных");
            Assert.That(Convert.ToHexString(SHA256.HashData(plain)), Is.EqualTo(vector.PlainSha256), $"{vector.Name}: распакованные данные не совпадают с эталоном ({vector.Notes})");
        });
    }

    [TestCaseSource(nameof(ChunkedVectorCases))]
    public void ReferenceVectorChunkedTest(object boxed, int chunk)
    {
        // Чтение мелкими кусками проверяет, что состояние декодера переживает границы вызовов Read.
        var vector = (ReferenceVector)boxed;
        var plain = Decompress(Convert.FromBase64String(vector.CompressedBase64), chunk);

        Assert.That(Convert.ToHexString(SHA256.HashData(plain)), Is.EqualTo(vector.PlainSha256), $"{vector.Name}: чтение кусками по {chunk} Б расходится с эталоном");
    }

    [Test]
    public void ReferenceVectorExactBytesTest()
    {
        // Для компактных образцов сверяем не отпечаток, а каждый байт.
        var checkedCount = 0;
        foreach (var vector in vectors)
        {
            if (vector.PlainBase64 is null) continue;

            var expected = Convert.FromBase64String(vector.PlainBase64);
            var plain = Decompress(Convert.FromBase64String(vector.CompressedBase64), chunk: 0);
            Assert.That(plain, Is.EqualTo(expected), $"{vector.Name}: побайтовое расхождение с эталоном");
            checkedCount++;
        }

        Assert.That(checkedCount, Is.GreaterThan(0), "Ни один образец не проверен побайтово");
    }

    [Test]
    public void ReferenceVectorsAreSelfConsistentTest()
    {
        // Страховка от опечатки в самих данных теста: длина и отпечаток должны сходиться.
        foreach (var vector in vectors)
        {
            if (vector.PlainBase64 is null) continue;

            var expected = Convert.FromBase64String(vector.PlainBase64);
            Assert.Multiple(() =>
            {
                Assert.That(expected, Has.Length.EqualTo(vector.PlainLength), $"{vector.Name}: длина образца не совпадает с объявленной");
                Assert.That(Convert.ToHexString(SHA256.HashData(expected)), Is.EqualTo(vector.PlainSha256), $"{vector.Name}: SHA-256 образца не совпадает с объявленным");
            });
        }
    }

    private static byte[] Decompress(byte[] compressed, int chunk)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var zstd = new ZstdStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();

        if (chunk == 0)
        {
            zstd.CopyTo(output);
            return output.ToArray();
        }

        var buffer = new byte[chunk];
        int read;
        while ((read = zstd.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
