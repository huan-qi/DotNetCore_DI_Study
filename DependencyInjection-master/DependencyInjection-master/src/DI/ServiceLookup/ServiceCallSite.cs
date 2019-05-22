// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// ����һ����¼����������õ����ͣ�DI�ڲ�ʹ�ô�����������ͷ�װʵ��������ʵ����������Ҫ����Ϣ��
    /// ��������6�������ࣺ
	/// 1. ConstantCallSite ����ע�����Ե���ģʽ�Ծ���ʵ��ע��ʱʹ�á�
    /// 2. ConstructorCallSite ����ע����������ע�ᣬҲ����ʵ��������ʱ�Թ��캯��ʵ������
    /// 3. FactoryCallSite ����ע�����Թ�����ʽ��
    /// 4. IEnumerableCallSite ����ǵ��õ�ǰ�������͵�����ʵ������GetServices()ʱ��һ���������Ϳ��Խ��ж�ε�ע�ᣩ
    /// 5. ServiceProviderCallSite ����ǻ�ȡ IServiceProvider ���͵ķ���ʵ����ʹ�õģ� �� ServiceProviderEngine ���л�ע�����ʵ����
    /// 6. ServiceScopeFactoryCallSite ����ǻ�ȡ IServiceScopeFactory ��ʹ�õģ� �� ServiceProviderEngine ���л�ע�����ʵ����Ȼ���ȡ����������
    /// </summary>
    internal abstract class ServiceCallSite
    {
        protected ServiceCallSite(ResultCache cache)
        {
            Cache = cache;
        }

        // ��ǰע��ķ�������
        public abstract Type ServiceType { get; }

        // ��ǰע���ʵ��������
        public abstract Type ImplementationType { get; }

        // ��ǰ CallSite ��������
        public abstract CallSiteKind Kind { get; }

        // ����ʵ������Ļ�������
        public ResultCache Cache { get; }
    }
}