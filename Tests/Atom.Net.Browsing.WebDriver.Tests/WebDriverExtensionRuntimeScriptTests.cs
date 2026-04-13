using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverExtensionRuntimeScriptTests
{
    [Test]
    public async Task GeneratedContentRuntimeIncludesVirtualMediaWarmupAndSyntheticVideoFallback()
    {
        var contentRuntimePath = IOPath.Combine(AppContext.BaseDirectory, "ExtensionWorkingLayout", "Extension", "content.js");

        Assert.That(File.Exists(contentRuntimePath), Is.True, $"Не найден сгенерированный content runtime: {contentRuntimePath}");

        var script = await File.ReadAllTextAsync(contentRuntimePath).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("const normalizeLabel = (value) => typeof value === 'string'"));
            Assert.That(script, Does.Contain("requestedDeviceId === 'default'"));
            Assert.That(script, Does.Contain("normalizeLabel(device.label) === expectedLabel"));
            Assert.That(script, Does.Contain("let warmupStream = null;"));
            Assert.That(script, Does.Contain("warmupStream = await originalGetUserMedia(warmupConstraints);"));
            Assert.That(script, Does.Contain("const createSyntheticVideoStream = (request) => {"));
            Assert.That(script, Does.Contain("return createSyntheticVideoStream(request.video);"));
            Assert.That(script, Does.Contain("return mergeStreams(nativeStream, createSyntheticVideoStream(request.video));"));
        });
    }
}