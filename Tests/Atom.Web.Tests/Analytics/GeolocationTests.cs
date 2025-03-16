using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Web.Analytics.Tests;

public class GeolocationTests(ILogger logger) : BenchmarkTest<GeolocationTests>(logger)
{
    public override bool IsBenchmarkDisabled => true;

    public GeolocationTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест сериализации"), Benchmark]
    public void SerializeTest()
    {
        var geolocation = Geolocation.Rent();

        geolocation.Latitude = 65.86785675;
        geolocation.Longitude = 146.76576576;

        var json = geolocation.Serialize();

        if (IsTest)
        {
            Assert.That(json, Is.Not.Null);
            Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "{\"latitude\":65.86785675,\"longitude\":146.76576576}"));
        }

        if (string.IsNullOrEmpty(json)) return;
        var geolocation2 = Geolocation.Deserialize(json);

        if (IsTest)
        {
            Assert.That(geolocation2, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(geolocation2.Latitude, Is.EqualTo(geolocation.Latitude));
                Assert.That(geolocation2.Longitude, Is.EqualTo(geolocation.Longitude));
            }
        }

        if (geolocation2 is not null) Geolocation.Return(geolocation2);

        geolocation.Continent = Continent.AF;
        geolocation.Country = Country.NGA;

        json = geolocation.Serialize();

        if (IsTest)
        {
            Assert.That(json, Is.Not.Null);
            Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "{\"continent\":\"AF\",\"country\":\"NGA\",\"latitude\":65.86785675,\"longitude\":146.76576576}"));
        }

        if (string.IsNullOrEmpty(json)) return;
        geolocation2 = Geolocation.Deserialize(json);

        if (IsTest)
        {
            Assert.That(geolocation2, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(geolocation2.Continent, Is.EqualTo(geolocation.Continent));
                Assert.That(geolocation2.Country, Is.EqualTo(geolocation.Country));
                Assert.That(geolocation2.Latitude, Is.EqualTo(geolocation.Latitude));
                Assert.That(geolocation2.Longitude, Is.EqualTo(geolocation.Longitude));
            }
        }

        if (geolocation2 is not null) Geolocation.Return(geolocation2);

        Geolocation.Return(geolocation);
    }
}