// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal class ConstructorCallSite : ServiceCallSite
    {
        // 实例化对象时所使用的构造器，当前构造器的最优构造器
        internal ConstructorInfo ConstructorInfo { get; }

        // 当前构造器中所有参数的集合，所有的参数的信息都会被存储为 ServiceCallSite （为了以后构建参数的实例对象）
        internal ServiceCallSite[] ParameterCallSites { get; }

        // 最优构造器为无参
        public ConstructorCallSite(ResultCache cache, Type serviceType, ConstructorInfo constructorInfo) : this(cache, serviceType, constructorInfo, Array.Empty<ServiceCallSite>())
        {
        }


        public ConstructorCallSite(ResultCache cache, Type serviceType, ConstructorInfo constructorInfo, ServiceCallSite[] parameterCallSites) : base(cache)
        {
            ServiceType = serviceType;
            ConstructorInfo = constructorInfo;
            ParameterCallSites = parameterCallSites;
        }

        public override Type ServiceType { get; }

        public override Type ImplementationType => ConstructorInfo.DeclaringType;
        public override CallSiteKind Kind { get; } = CallSiteKind.Constructor;
    }
}
