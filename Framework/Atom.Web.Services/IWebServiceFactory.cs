using Atom.Architect;
using Atom.Architect.Components;

namespace Atom.Web.Services;

/// <summary>
/// Представляет базовый интерфейс для реализации фабрик для работы с веб-сервисами.
/// </summary>
/// <typeparam name="TService">Тип базового интерфейса связанного сервиса.</typeparam>
/// <typeparam name="TFactory">Тип реализуемой фабрики.</typeparam>
public interface IWebServiceFactory<TService, out TFactory> : IModular<TService, TFactory>, IFactory, IDisposable
    where TService : IWebService
    where TFactory : IFactory
{
    /// <summary>
    /// Коллекция подключенных сервисов.
    /// </summary>
    IEnumerable<TService> Services { get; }
}