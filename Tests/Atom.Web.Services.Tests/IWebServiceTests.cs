using Atom;
using Atom.Architect.Components;

namespace Atom.Web.Services.Tests;

public class IWebServiceTests(ILogger logger) : BenchmarkTests<IWebServiceTests>(logger)
{
    public IWebServiceTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "IWebService: объединяет IComponent и IDisposable")]
    public void ContractTest()
    {
        using var service = new TestWebService();
        var component = (IComponent)service;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(service, Is.InstanceOf<IComponent>());
        Assert.That(service, Is.InstanceOf<IDisposable>());
        Assert.That(service.Owner, Is.Null);
        Assert.That(component.IsAttached, Is.False);
    }

    [TestCase(TestName = "IWebService: AttachTo и Detach обновляют Owner")]
    public void AttachmentLifecycleTest()
    {
        using var service = new TestWebService();
        var component = (IComponent)service;
        var owner = new TestOwner();

        service.AttachTo(owner);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(service.Owner, Is.SameAs(owner));
        Assert.That(component.IsAttached, Is.True);

        service.Detach();

        Assert.That(service.Owner, Is.Null);
        Assert.That(component.IsAttached, Is.False);
    }

    private sealed class TestWebService : IWebService
    {
        public IComponentOwner? Owner { get; private set; }

        public bool IsAttachedByOwner { get; init; }

        public event MutableEventHandler<object, ComponentEventArgs>? Attached;

        public event MutableEventHandler<object, ComponentEventArgs>? Detached;

        public void AttachTo(IComponentOwner owner)
        {
            ArgumentNullException.ThrowIfNull(owner);

            Owner = owner;
            Attached?.Invoke(this, new ComponentEventArgs { Owner = owner, Component = this });
        }

        public void Detach()
        {
            var owner = Owner;
            Owner = null;
            Detached?.Invoke(this, new ComponentEventArgs { Owner = owner, Component = this });
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestOwner : IComponentOwner
    {
        public T Get<T>() where T : IComponent => throw new NotSupportedException();

        public IEnumerable<T> GetAll<T>() where T : IComponent => [];

        public bool Has<T>() where T : IComponent => false;

        public bool Has<T>(T component) where T : IComponent => false;

        public bool IsSupported<T>() where T : IComponent => false;

        public bool TryGet<T>(out T? component) where T : IComponent
        {
            component = default;
            return false;
        }

        public bool TryGetAll<T>(out IEnumerable<T> components) where T : IComponent
        {
            components = [];
            return false;
        }
    }
}