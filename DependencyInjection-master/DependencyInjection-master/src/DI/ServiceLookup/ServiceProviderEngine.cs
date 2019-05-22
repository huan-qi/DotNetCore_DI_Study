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

        private readonly Func<Type, Func<ServiceProviderEngineScope, object>> _createServiceAccessor; // ���ί�з������������һ���������ͣ�����ֵ�Ǵ��������ͷ���ʵ����ί�з�����
                                                                                                      // ��ί�л��ڹ��캯���б� CreateServiceAccessor ������ֵ��

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

       internal ConcurrentDictionary<Type, Func<ServiceProviderEngineScope, object>> RealizedServices { get; } //�洢��������ʵ������ί�з�����һ���ֵ䡣�����������ͣ� ֵ����������ʵ���ķ���������������ʵ�������Ľ���ֵ��һ�� ServiceProviderEngineScope ���͵Ĳ���������ֵ��һ�� object��
                                                                                                               //���ֶλ��ڹ��캯���г�ʼ��
                                                                                                               //���ֵ��ֵ��������ʱ��̬�ļ���
        internal CallSiteFactory CallSiteFactory { get; } //����ֶθ���ͨ�� ServiceDescription ���� ServiceCallSite ���������� ServiceCallSite ���д洢����¼�˴�������ʵ������Ҫ��������Ϣ�������ֶ���������ʱ������д����ġ�
                                                          //����ֶλ��ڹ��캯���г�ʼ��
        protected CallSiteRuntimeResolver RuntimeResolver { get; } // ����ֶδ洢��һ�����󣬸ö����Ǵ�������ʵ���ĸ������ڡ����������ڹ��캯���г�ʼ����

        public ServiceProviderEngineScope Root { get; }

        public IServiceScope RootScope => Root;

        public object GetService(Type serviceType) => GetService(serviceType, Root); //

        protected abstract Func<ServiceProviderEngineScope, object> RealizeService(ServiceCallSite callSite); // �ó��󷽷��Ǿ���Ĵ�������ʵ���ķ������� ServiceProviderEngine ������ʵ�֡�

        public void Dispose()
        {
            _disposed = true;
            Root.Dispose();
        }

        internal object GetService(Type serviceType, ServiceProviderEngineScope serviceProviderEngineScope) //��ȡ�������Ͷ�Ӧ�ķ���ʵ�������Ȼ�� RealizedServices �л�ȡ�������Ͷ�Ӧ�Ĵ���ʵ������ �����û�оͻ��������ǽ��÷������� RealizedServices������ʱ��̬��ӣ���
                                                                                                            //��ȡ�Ĵ���ʵ����ί�з�����Ҫһ������Ϊ ServiceProviderEngineScope �Ĳ������ܵ��ã����ط���ʵ����
                                                                                                            //���������������������ʵ���ķ�����
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

        private Func<ServiceProviderEngineScope, object> CreateServiceAccessor(Type serviceType) // ��������������Ǹ��ݷ������ͣ�������Ӧ�Ĳ�������ʵ����ί�з�����
                                                                                                 // ����������� CallSiteFactory �в�������������Ͷ�Ӧ�Ĵ�������ʵ���йص���Ϣ�������õ��� callSite ��Ϣ����������ʵ�ֵ� RealizeService ��������� RealizeService �������ء�
                                                                                                 // ��� CallSiteFactory ��û�ж�Ӧ����Ϣ�� �ͻὫ CallSiteFactory ���ڲ�����������Ϣ������ʱ��̬��ӣ�
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