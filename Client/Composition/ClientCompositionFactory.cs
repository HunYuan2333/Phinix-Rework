using System;
using System.Collections.Generic;
using Autofac;
using PhinixClient.Framework;

namespace PhinixClient.Framework
{
    public sealed class ClientCompositionFactory : IClientCompositionFactory, IDisposable
    {
        private readonly Func<bool> isMainThread;
        private readonly Action<Exception> report;
        private readonly List<Scope> scopes = new List<Scope>();
        private bool disposed;

        public ClientCompositionFactory(Func<bool> isMainThread, Action<Exception> report)
        {
            this.isMainThread = isMainThread ?? throw new ArgumentNullException(nameof(isMainThread));
            this.report = report;
        }

        private void CheckThread()
        {
            if (!isMainThread()) throw new InvalidOperationException("Client composition requires the game main thread.");
        }

        private void Report(Exception error)
        {
            // A failing logger must not prevent cleanup of other components.
            try { report?.Invoke(error); } catch { }
        }

        public IClientCompositionScope CreateScope(Action<IClientCompositionBuilder> configure)
        {
            CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(ClientCompositionFactory));
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            var scope = new Scope(this);
            var builder = new Builder(scope);
            try
            {
                configure(builder);
                builder.Close();
                scope.Attach(builder.Container.Build());
                scopes.Add(scope);
                return scope;
            }
            catch
            {
                builder.Close();
                scope.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            CheckThread();
            if (disposed) return;
            disposed = true;
            Scope[] pending = scopes.ToArray();
            scopes.Clear();
            for (int index = pending.Length - 1; index >= 0; index--)
                pending[index].Dispose();
        }

        private sealed class Builder : IClientCompositionBuilder
        {
            internal readonly ContainerBuilder Container = new ContainerBuilder();
            private readonly Scope scope;
            private bool closed;
            public Builder(Scope scope) { this.scope = scope; }
            internal void Close() { closed = true; }
            private void Check()
            {
                scope.Owner.CheckThread();
                if (closed) throw new InvalidOperationException("Composition registration is already complete.");
            }
            public void Borrow<T>(T instance) where T : class
            {
                Check();
                if (instance == null) throw new ArgumentNullException(nameof(instance));
                Container.RegisterInstance(instance).As<T>().ExternallyOwned();
            }
            public void Register<TService, TImplementation>() where TService : class where TImplementation : class, TService
            {
                Check();
                if (typeof(IAsyncDisposable).IsAssignableFrom(typeof(TImplementation))
                    && !typeof(IDisposable).IsAssignableFrom(typeof(TImplementation)))
                    throw new InvalidOperationException("Owned client resources must provide synchronous IDisposable cleanup on the main thread.");
                Container.RegisterType<TImplementation>().As<TService>().AsSelf()
                    .SingleInstance().OnRelease(value => scope.Release(value));
            }
        }

        private sealed class Scope : IClientCompositionScope
        {
            internal readonly ClientCompositionFactory Owner;
            private IContainer container;
            private bool disposed;
            internal Scope(ClientCompositionFactory owner) { Owner = owner; }
            internal void Attach(IContainer value) { container = value; }
            public T Resolve<T>() where T : class
            {
                Owner.CheckThread();
                if (disposed) throw new ObjectDisposedException("ClientCompositionScope");
                if (container == null) throw new InvalidOperationException("Resolve requires completed registration.");
                return container.Resolve<T>();
            }
            internal void Release(object value)
            {
                try { (value as IDisposable)?.Dispose(); }
                catch (Exception error) { Owner.Report(error); }
            }
            public void Dispose()
            {
                Owner.CheckThread();
                if (disposed) return;
                disposed = true;
                try { container?.Dispose(); }
                catch (Exception error) { Owner.Report(error); }
                finally { container = null; Owner.scopes.Remove(this); }
            }
        }
    }
}
