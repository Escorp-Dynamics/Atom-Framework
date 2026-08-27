using System.Text.Json.Nodes;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Общий managed policy файл обслуживает сразу несколько запусков драйвера — в том числе из
/// разных процессов. Записи обязаны складываться, а не затирать друг друга.
/// </summary>
public sealed class WebDriverManagedPolicyRegistryTests
{
    private string policyPath = string.Empty;

    [SetUp]
    public void SetUp()
        => policyPath = Path.Combine(Path.GetTempPath(), $"atom-policy-{Guid.NewGuid():N}.json");

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (File.Exists(policyPath))
                File.Delete(policyPath);
        }
        catch (IOException)
        {
            // Временный файл; не мешаем прогону из-за неудачной уборки.
        }
    }

    [Test]
    public void BuildPolicyWithEntryWhenSecondLaunchPublishesKeepsBothEntries()
    {
        var first = CreateExtensionId();
        var second = CreateExtensionId();

        File.WriteAllText(policyPath, BridgeManagedPolicyRegistry.BuildPolicyWithEntry(policyPath, first, CreateEntryPolicy(first)));
        var merged = BridgeManagedPolicyRegistry.BuildPolicyWithEntry(policyPath, second, CreateEntryPolicy(second));

        var policy = JsonNode.Parse(merged)!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(ReadForcelistIds(policy), Is.EquivalentTo(new[] { first, second }));
            Assert.That(ReadSettingsIds(policy), Is.EquivalentTo(new[] { first, second }));
        });

        Cleanup(first, second);
    }

    [Test]
    public void BuildPolicyWithoutEntryWhenAnotherLaunchStillRunningKeepsItsEntry()
    {
        var mine = CreateExtensionId();
        var neighbour = CreateExtensionId();

        File.WriteAllText(policyPath, BridgeManagedPolicyRegistry.BuildPolicyWithEntry(policyPath, mine, CreateEntryPolicy(mine)));
        File.WriteAllText(policyPath, BridgeManagedPolicyRegistry.BuildPolicyWithEntry(policyPath, neighbour, CreateEntryPolicy(neighbour)));

        var remaining = BridgeManagedPolicyRegistry.BuildPolicyWithoutEntry(policyPath, mine);

        Assert.That(remaining, Is.Not.Null, "Пока жив соседний запуск, файл удалять нельзя.");
        var policy = JsonNode.Parse(remaining!)!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(ReadForcelistIds(policy), Is.EquivalentTo(new[] { neighbour }));
            Assert.That(ReadSettingsIds(policy), Is.EquivalentTo(new[] { neighbour }));
        });

        Cleanup(neighbour);
    }

    [Test]
    public void BuildPolicyWithoutEntryWhenLastEntryRemovedRequestsFileRemoval()
    {
        var only = CreateExtensionId();
        File.WriteAllText(policyPath, BridgeManagedPolicyRegistry.BuildPolicyWithEntry(policyPath, only, CreateEntryPolicy(only)));

        Assert.That(BridgeManagedPolicyRegistry.BuildPolicyWithoutEntry(policyPath, only), Is.Null);
    }

    /// <summary>
    /// Записи, оставшиеся от процессов, которые уже не живут (падение, kill), обязаны сниматься:
    /// их update URL указывает на мёртвый порт и ломает каждый последующий старт браузера.
    /// </summary>
    [Test]
    public void BuildPolicyWithEntryWhenFileHoldsAbandonedEntryDropsIt()
    {
        var abandoned = CreateExtensionId();
        var mine = CreateExtensionId();

        var abandonedPolicy = new JsonObject
        {
            ["ExtensionInstallForcelist"] = new JsonArray($"{abandoned};https://127.0.0.1:1/chromium/{abandoned}/manifest"),
            ["ExtensionSettings"] = new JsonObject { [abandoned] = new JsonObject { ["installation_mode"] = "force_installed" } },
        };
        File.WriteAllText(policyPath, abandonedPolicy.ToJsonString());

        var merged = BridgeManagedPolicyRegistry.BuildPolicyWithEntry(policyPath, mine, CreateEntryPolicy(mine));
        var policy = JsonNode.Parse(merged)!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(ReadForcelistIds(policy), Is.EquivalentTo(new[] { mine }));
            Assert.That(ReadSettingsIds(policy), Does.Not.Contain(abandoned));
        });

        Cleanup(mine);
    }

    private void Cleanup(params string[] extensionIds)
    {
        foreach (var extensionId in extensionIds)
            _ = BridgeManagedPolicyRegistry.BuildPolicyWithoutEntry(policyPath, extensionId);
    }

    private static string CreateExtensionId() => Guid.NewGuid().ToString("N")[..32];

    private static JsonObject CreateEntryPolicy(string extensionId)
        => new()
        {
            ["ExtensionInstallForcelist"] = new JsonArray($"{extensionId};https://127.0.0.1:9/chromium/{extensionId}/manifest"),
            ["ExtensionSettings"] = new JsonObject
            {
                [extensionId] = new JsonObject
                {
                    ["installation_mode"] = "force_installed",
                    ["update_url"] = $"https://127.0.0.1:9/chromium/{extensionId}/manifest",
                },
            },
        };

    private static string[] ReadForcelistIds(JsonObject policy)
        => policy["ExtensionInstallForcelist"] is JsonArray forcelist
            ? forcelist.Select(static node => node!.GetValue<string>().Split(';')[0]).ToArray()
            : [];

    private static string[] ReadSettingsIds(JsonObject policy)
        => policy["ExtensionSettings"] is JsonObject settings
            ? settings.Select(static pair => pair.Key).ToArray()
            : [];
}
