// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// 这是一个记录服务访问配置的类型，DI内部使用此类的派生类型封装实例化服务实例类型所需要的信息。
    /// 该类型有6个派生类：
	/// 1. ConstantCallSite 服务注册是以单例模式以具体实例注册时使用。
    /// 2. ConstructorCallSite 服务注册是以类型注册，也就是实例化对象时以构造函数实例化。
    /// 3. FactoryCallSite 服务注册是以工厂形式。
    /// 4. IEnumerableCallSite 这个是调用当前服务类型的所有实例，在GetServices()时（一个服务类型可以进行多次的注册）
    /// 5. ServiceProviderCallSite 这个是获取 IServiceProvider 类型的服务实例所使用的， 在 ServiceProviderEngine 类中会注册此类实例。
    /// 6. ServiceScopeFactoryCallSite 这个是获取 IServiceScopeFactory 所使用的， 在 ServiceProviderEngine 类中会注册此类实例，然后获取子类容器。
    /// </summary>
    internal abstract class ServiceCallSite
    {
        protected ServiceCallSite(ResultCache cache)
        {
            Cache = cache;
        }

        // 当前注册的服务类型
        public abstract Type ServiceType { get; }

        // 当前注册的实例化类型
        public abstract Type ImplementationType { get; }

        // 当前 CallSite 所属类型
        public abstract CallSiteKind Kind { get; }

        // 服务实例对象的缓存配置
        public ResultCache Cache { get; }
    }
}