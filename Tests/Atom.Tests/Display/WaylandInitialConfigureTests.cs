using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.Versioning;

using Atom.Display.Wayland;

namespace Atom.Tests;

/// <summary>
/// Размер окна в первом configure композитора.
/// </summary>
/// <remarks>
/// ★ Firefox на Wayland не применяет свои <c lang="text">-width/-height</c> и на configure(0, 0) встаёт
/// в минимум 500×200. Chrome же на ноль берёт свой <c lang="text">--window-size</c>, и навязанный
/// размер ему вреден. Поэтому проверяются обе ветви: ноль по умолчанию и заданный размер.
/// </remarks>
[TestFixture]
[Category("Display")]
[SupportedOSPlatform("linux")]
public class WaylandInitialConfigureTests
{
    [SetUp]
    public void SetUp()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Композитор Wayland работает только на Linux.");
    }

    [Test(Description = "Без заданного размера окно решает само: configure(0, 0)")]
    public async Task InitialConfigureShouldLeaveSizeToClientByDefault()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        var window = client.CreateWindow(Title);

        Assert.That(WaitForToplevelConfigure(client, window.Toplevel), Is.EqualTo(Size.Empty));
    }

    [Test(Description = "Заданный размер приходит окну в первом configure")]
    public async Task InitialConfigureShouldCarryPreferredSize()
    {
        await using var compositor = CreateCompositor();
        compositor.PreferredWindowSize = PreferredSize;
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        var window = client.CreateWindow(Title);

        Assert.That(WaitForToplevelConfigure(client, window.Toplevel), Is.EqualTo(PreferredSize));
    }

    [Test(Description = "Заданный размер получает и каждое следующее окно")]
    public async Task EveryNewWindowShouldGetPreferredSize()
    {
        await using var compositor = CreateCompositor();
        compositor.PreferredWindowSize = PreferredSize;
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        _ = WaitForToplevelConfigure(client, client.CreateWindow(Title).Toplevel);
        var second = client.CreateWindow(Title + " 2");

        Assert.That(WaitForToplevelConfigure(client, second.Toplevel), Is.EqualTo(PreferredSize));
    }

    private static WaylandCompositor CreateCompositor()
        => WaylandCompositor.Create(new WaylandCompositorSettings
        {
            Resolution = new Size(1920, 1080),
            EnablePresentation = false,
        });

    private static Size WaitForToplevelConfigure(WaylandTestClient client, uint toplevel)
    {
        for (var attempt = 0; attempt < WaitAttempts; ++attempt)
        {
            foreach (var (objectId, opcode, body) in client.DrainEvents())
            {
                if (objectId != toplevel || opcode != ToplevelConfigure)
                    continue;

                return new Size(
                    BinaryPrimitives.ReadInt32LittleEndian(body),
                    BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4)));
            }

            Thread.Sleep(PollDelay);
        }

        Assert.Fail("Окно так и не получило configure.");
        return default;
    }

    private const string Title = "Окно";
    private const ushort ToplevelConfigure = 0;
    private const int WaitAttempts = 100;
    private const int PollDelay = 20;

    private static readonly Size PreferredSize = new(1440, 900);
}
