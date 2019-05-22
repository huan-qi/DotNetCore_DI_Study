// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal class ServiceProviderEngineScope : IServiceScope, IServiceProvider  //���������һ���������ͣ� ���ʵ�����ķ������ͻ����ڴ���֮��
    {
        // For testing only
        internal Action<object> _captureDisposableCallback;

        private List<IDisposable> _disposables; //���ֶλ����������ʵ���� IDisposable �ӿڵ�ע������Ա����ͷŴ�����

        private bool _disposed; //�ж������Ƿ��ѱ��ͷ�

        public ServiceProviderEngineScope(ServiceProviderEngine engine)
        {
            Engine = engine;
        }

        internal Dictionary<ServiceCacheKey, object> ResolvedServices { get; } = new Dictionary<ServiceCacheKey, object>(); //����ʵ������ļ��ϣ�ʹ�� ServiceCacheKey ��Ϊ����� key

        // ����ServiceProviderEngineScope������һ��ServiceProviderEngine
        public ServiceProviderEngine Engine { get; } // �������ͣ�������ͨ�����캯�����룬��������������һ�� ServiceProviderEngine��Ҳ���ǹ�����������ע��ķ���

        //summary
        //��ȡ���󣬿��Կ����˷������õ� Engine �� GetService()
        public object GetService(Type serviceType)
        {
            if (_disposed)
            {
                ThrowHelper.ThrowObjectDisposedException();
            }

            return Engine.GetService(serviceType, this);
        }

        //summary
        //����������ص��ǵ�ǰ����
        public IServiceProvider ServiceProvider => this; 

        //summary
        //�ͷŵ�ǰ������
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
        //�����������Ҫ���ͷŵķ���ʵ��
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