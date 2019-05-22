// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal abstract class ServiceProviderEngine : IServiceProviderEngine, IServiceScopeFactory
    {
        private readonly IServiceProviderEngineCallback _callback;

        private readonly Func<Type, Func<ServiceProviderEngineScope, object>> _createServiceAccessor; // 这个委托方法输入参数是一个服务类型，返回值是创建该类型服务实例的委托方法。
                                                                                                      // 该委托会在构造函数中被 CreateServiceAccessor 方法赋值。

        private bool _disposed;

        protected ServiceProviderEngine(IEnumerable<ServiceDescriptor> serviceDescriptors, IServiceProviderEngineCallback callback)
        {
            _createServiceAccessor = CreateServiceAccessor;
            _callback = callback;
            Root = new ServiceProviderEngineScope(this);
            RuntimeResolver = new CallSiteRuntimeResolver();
            CallSiteFactory = new CallSiteFactory(serviceDescriptors);
            CallSiteFactory.Add(typeof(IServiceProvider), new ServiceProviderCallSite());
            CallSiteFactory.Add(typeof(IServiceScopeFactory), new ServiceScopeFactoryCallSite());
            RealizedServices = new ConcurrentDictionary<Type, Func<ServiceProviderEngineScope, object>>();
        }

       internal ConcurrentDictionary<Type, Func<ServiceProviderEngineScope, object>> RealizedServices { get; } //存储创建服务实例具体委托方法的一个字典。键：服务类型， 值：创建服务实例的方法。（创建服务实例方法的接收值是一个 ServiceProviderEngineScope 类型的参数，返回值是一个 object）
                                                                                                               //该字段会在构造函数中初始化
                                                                                                               //该字典的值会在运行时动态的加入
        internal CallSiteFactory CallSiteFactory { get; } //这个字段负责通过 ServiceDescription 创建 ServiceCallSite 并将创建的 ServiceCallSite 进行存储（记录了创建服务实例所需要的所有信息），该字段是在运行时对其进行创建的。
                                                          //这个字段会在构造函数中初始化
        protected CallSiteRuntimeResolver RuntimeResolver { get; } // 这个字段存储了一个对象，该对象是创建服务实例的根本所在。这个对象会在构造函数中初始化。

        public ServiceProviderEngineScope Root { get; }

        public IServiceScope RootScope => Root;

        public object GetService(Type serviceType) => GetService(serviceType, Root); //

        protected abstract Func<ServiceProviderEngineScope, object> RealizeService(ServiceCallSite callSite); // 该抽象方法是具体的创建服务实例的方法，由 ServiceProviderEngine 的子类实现。

        public void Dispose()
        {
            _disposed = true;
            Root.Dispose();
        }

        internal object GetService(Type serviceType, ServiceProviderEngineScope serviceProviderEngineScope) //获取服务类型对应的服务实例，首先会从 RealizedServices 中获取服务类型对应的创建实例方法 （如果没有就会在运行是将该方法加入 RealizedServices，运行时动态添加）。
                                                                                                            //获取的创建实例的委托方法需要一个类型为 ServiceProviderEngineScope 的参数才能调用，返回服务实例。
                                                                                                            //最后调用这个创建服务类型实例的方法。
        {
            if (_disposed)
            {
                ThrowHelper.ThrowObjectDisposedException();
            }

            var realizedService = RealizedServices.GetOrAdd(serviceType, _createServiceAccessor);
            _callback?.OnResolve(serviceType, serviceProviderEngineScope);
            return realizedService.Invoke(serviceProviderEngineScope);
        }

        public IServiceScope CreateScope()
        {
            if (_disposed)
            {
                ThrowHelper.ThrowObjectDisposedException();
            }

            return new ServiceProviderEngineScope(this);
        }

        private Func<ServiceProviderEngineScope, object> CreateServiceAccessor(Type serviceType) // 这个方法的作用是根据服务类型，创建对应的产生服务实例的委托方法。
                                                                                                 // 这个方法会在 CallSiteFactory 中查找输入服务类型对应的创建服务实例有关的信息。并将得到的 callSite 信息传入由子类实现的 RealizeService 方法。最后将 RealizeService 方法返回。
                                                                                                 // 如果 CallSiteFactory 中没有对应的信息， 就会将 CallSiteFactory 在内部新添加这个信息（运行时动态添加）
        {
            var callSite = CallSiteFactory.GetCallSite(serviceType, new CallSiteChain());
            if (callSite != null)
            {
                _callback?.OnCreate(callSite);
                return RealizeService(callSite);
            }

            return _ => null;
        }
    }
}