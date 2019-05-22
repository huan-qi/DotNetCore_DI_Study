// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal class ConstructorCallSite : ServiceCallSite
    {
        // ʵ��������ʱ��ʹ�õĹ���������ǰ�����������Ź�����
        internal ConstructorInfo ConstructorInfo { get; }

        // ��ǰ�����������в����ļ��ϣ����еĲ�������Ϣ���ᱻ�洢Ϊ ServiceCallSite ��Ϊ���Ժ󹹽�������ʵ������
        internal ServiceCallSite[] ParameterCallSites { get; }

        // ���Ź�����Ϊ�޲�
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
