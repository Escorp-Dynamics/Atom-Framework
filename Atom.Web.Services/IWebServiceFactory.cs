using Atom.Architect;
using Atom.Architect.Components;

namespace Atom.Web.Services;

/// <summary>
/// Представляет базовый интерфейс для реализации фабрик для работы с веб-сервисами.
/// </summary>
/// <typeparam name="TFactory">Тип реализуемой фабрики.</typeparam>
/// <typeparam name="TService">Тип базового интерфейса связанного сервиса.</typeparam>
public interface IWebServiceFactory<TFactory, TService> : IFactory, IModular<TService>, IDisposable
    where TFactory : IFactory
    where TService : IWebService
{
    /// <summary>
    /// Коллекция подключенных сервисов.
    /// </summary>
    IEnumerable<TService> Services { get; }
}