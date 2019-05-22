// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal class ServiceProviderEngineScope : IServiceScope, IServiceProvider  //这个类型是一个容器类型， 最后实例化的服务对象就缓存在此类之中
    {
        // For testing only
        internal Action<object> _captureDisposableCallback;

        private List<IDisposable> _disposables; //此字段缓存的是所有实现了 IDisposable 接口的注册服务，以便在释放此容器

        private bool _disposed; //判断属性是否已被释放

        public ServiceProviderEngineScope(ServiceProviderEngine engine)
        {
            Engine = engine;
        }

        internal Dictionary<ServiceCacheKey, object> ResolvedServices { get; } = new Dictionary<ServiceCacheKey, object>(); //缓存实例对象的集合，使用 ServiceCacheKey 作为缓存的 key

        // 所有ServiceProviderEngineScope对象共享一个ServiceProviderEngine
        public ServiceProviderEngine Engine { get; } // 引擎类型，此属性通过构造函数传入，并所有容器共享一个 ServiceProviderEngine，也就是共享容器共享注册的服务。

        //summary
        //获取对象，可以看到此方法调用的 Engine 的 GetService()
        public object GetService(Type serviceType)
        {
            if (_disposed)
            {
                ThrowHelper.ThrowObjectDisposedException();
            }

            return Engine.GetService(serviceType, this);
        }

        //summary
        //这个方法返回的是当前对象
        public IServiceProvider ServiceProvider => this; 

        //summary
        //释放当前容器，
        public void Dispose()
        {
            lock (ResolvedServices)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_disposables != null)
                {
                    for (var i = _disposables.Count - 1; i >= 0; i--)
                    {
                        var disposable = _disposables[i];
                        disposable.Dispose();
                    }

                    _disposables.Clear();
                }

                ResolvedServices.Clear();
            }
        }

        //summary
        //这个方法缓存要被释放的服务实例
        internal object CaptureDisposable(object service)
        {
            _captureDisposableCallback?.Invoke(service);

            if (!ReferenceEquals(this, service))
            {
                if (service is IDisposable disposable)
                {
                    lock (ResolvedServices)
                    {
                        if (_disposables == null)
                        {
                            _disposables = new List<IDisposable>();
                        }

                        _disposables.Add(disposable);
                    }
                }
            }
            return service;
        }
    }
}