// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// ��������� ServiceCallSite �Ĺ������ͣ��ڲ����� ServiceDescriptor ������Ӧ�� ServiceCallSite
    /// </summary>
    internal class CallSiteFactory
    {
        // ��������Ĭ�ϵ�Slot��Ĭ��Ϊ 0
        private const int DefaultSlot = 0;
		
        // �������ǻ������е�ServiceDescriptor
        private readonly List<ServiceDescriptor> _descriptors;
		
		// ServiceCallSite �Ļ��漯��
		// ServiceCallSite  �Ǵ�������ʵ�������ݣ������˷������ͣ�ʵ�����ͣ�CallSite ���࣬ʵ������Ļ�������
        private readonly ConcurrentDictionary<Type, ServiceCallSite> _callSiteCache = new ConcurrentDictionary<Type, ServiceCallSite>();
		
		// ServiceDescriptorCacheItem �Ļ��漯�� 
		// ��Ϊע������ʱ���һû����֤ʵ�����͵���Ч�ԣ��ӿڣ�������ȣ����������ǿ������ͬһ��������ж��ע�ᣬ�Զ��ע��ķ���΢���������ȷ�������Ķ����أ� 
		// �����Щ���⣬΢�������һ���ṹ�壬������һ��������������ע���ʵ������ ��ServiceDescriptorCacheItem��
		// ��һ��ע���Ԫ���ݴ洢�� _item �У������÷��������Ԫ���ݶ��洢�� _items �У���Ĭ��������ͬ���һ��Ԫ���ݡ�
        private readonly Dictionary<Type, ServiceDescriptorCacheItem> _descriptorLookup = new Dictionary<Type, ServiceDescriptorCacheItem>();
	
        private readonly StackGuard _stackGuard;

        public CallSiteFactory(IEnumerable<ServiceDescriptor> descriptors)
        {
            _stackGuard = new StackGuard();
            _descriptors = descriptors.ToList();
            Populate(descriptors);
        }
		
        // �����Ǵ����� IServiceCollection ���񼯺�ת�浽 _descriptorLookup �С�
		// ���Ȼ����һЩ�е��жϣ������л���
        private void Populate(IEnumerable<ServiceDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
				// ��ȡServiceDescription��������ע��ķ�������
                var serviceTypeInfo = descriptor.ServiceType.GetTypeInfo();
                if (serviceTypeInfo.IsGenericTypeDefinition)
                {
					// ��ȡ��ǰ����ʵ�����͵�����
                    var implementationTypeInfo = descriptor.ImplementationType?.GetTypeInfo();
					// ���ʵ������implementationTypeInfo����Ϊ�գ����߲��Ƿ������ͣ����׳��쳣��
                    if (implementationTypeInfo == null || !implementationTypeInfo.IsGenericTypeDefinition)
                    {
                        throw new ArgumentException(
                            Resources.FormatOpenGenericServiceRequiresOpenGenericImplementation(descriptor.ServiceType),
                            nameof(descriptors));
                    }
					// ���ʵ������implementationTypeInfo����Ϊ�������ͣ�����Ϊ�ӿڣ����׳��쳣��
                    if (implementationTypeInfo.IsAbstract || implementationTypeInfo.IsInterface)
                    {
                        throw new ArgumentException(
                            Resources.FormatTypeCannotBeActivated(descriptor.ImplementationType, descriptor.ServiceType));
                    }
                }
                else if (descriptor.ImplementationInstance == null && descriptor.ImplementationFactory == null)
                {
                    Debug.Assert(descriptor.ImplementationType != null);
                    var implementationTypeInfo = descriptor.ImplementationType.GetTypeInfo();
					// �����ǰ����ʵ�����Ͳ�Ϊ������
					// ����ʵ������Ϊ�������ӿ����ͣ����׳��쳣
                    if (implementationTypeInfo.IsGenericTypeDefinition ||
                        implementationTypeInfo.IsAbstract ||
                        implementationTypeInfo.IsInterface)
                    {
                        throw new ArgumentException(
                            Resources.FormatTypeCannotBeActivated(descriptor.ImplementationType, descriptor.ServiceType));
                    }
                }

				// ʹ�÷���������Ϊkey������ ServiceDescriptor ���浽 Dictionary<Type, ServiceDescriptorCacheItem> ������
                var cacheKey = descriptor.ServiceType;
				
				// ���� ServiceDescriptorCacheItem ��һ���ṹ�����Բ����쳣
                _descriptorLookup.TryGetValue(cacheKey, out var cacheItem);
                _descriptorLookup[cacheKey] = cacheItem.Add(descriptor);
            }
        }

		// GetCallSite() �������ⲿҲ�ǵ��ô˷������л�ȡ ServiceCallSite�������ǰ ServiceCallSite �ѱ����棬��ֱ�ӻ�ȡ���������ݣ�
		// ���δ���棬�򴴽������档������ CreateCallSite() ���л��棩
		// ��ǰ��������һ�� CallSiteChain ���ͣ����������һ�����ƣ�Ӧ����Ϊ�˷�ֹ���̣߳��ڴ���֮ǰ�������жϣ�����Ѵ��������׳��쳣��CallSiteChain ������ڴ˾Ͳ������ܡ�
        internal ServiceCallSite GetCallSite(Type serviceType, CallSiteChain callSiteChain)
        {
#if NETCOREAPP2_0
            return _callSiteCache.GetOrAdd(serviceType, (type, chain) => CreateCallSite(type, chain), callSiteChain);
#else
            return _callSiteCache.GetOrAdd(serviceType, type => CreateCallSite(type, callSiteChain));
#endif
        }

		// ��CreateCallSite() ���ȵ����� CallSiteChain ʵ���� CheckCircularDependency() ���������������������ѱ�����,���׳��쳣.
		// Ȼ��ֱ����TryCreateExact(),TryCreateOpenGeneric(),TryCreateEnumerable()�������������г���ʵ���� ServiceCallSite 
        private ServiceCallSite CreateCallSite(Type serviceType, CallSiteChain callSiteChain)
        {
            if (!_stackGuard.TryEnterOnCurrentStack())
            {
                return _stackGuard.RunOnEmptyStack((type, chain) => CreateCallSite(type, chain), serviceType, callSiteChain);
            }

            ServiceCallSite callSite;
            try
            {
				// ����Ƿ��ѱ�����,����Ѵ���,���׳��쳣
                callSiteChain.CheckCircularDependency(serviceType);
				// ��ȡָ�������ʵ������ʽ
				//   1.���ȴ�����ͨ���͵�ServiceCallSite,
				//   2.�����������͵�ServiceCallSite
				//   3.������������Ǽ���.��ô����ȡ��ǰ��������ʵ�ֶ���
                callSite = TryCreateExact(serviceType, callSiteChain) ??
                           TryCreateOpenGeneric(serviceType, callSiteChain) ??
                           TryCreateEnumerable(serviceType, callSiteChain);
            }
            finally
            {
                callSiteChain.Remove(serviceType);
            }

            _callSiteCache[serviceType] = callSite;

            return callSite;
        }

		// TryCreateExact()�����������������ֻ��һ����ͨ����ʱ��ʹ�õķ���,���´���,�����ж��˴������Ƿ������ _descriptorLookup �����У�
		// ���������ֱ�ӷ���null,������ڵĻ�ֱ��ʹ�����һ�� ServiceDescriptor �� DefaultSlot��
        private ServiceCallSite TryCreateExact(Type serviceType, CallSiteChain callSiteChain)
        {
            if (_descriptorLookup.TryGetValue(serviceType, out var descriptor))
            {
                // ָ�� _descriptorLookup �л�������з���ʵ�������е����һ����Ϊʵ�����͡�
                return TryCreateExact(descriptor.Last, serviceType, callSiteChain, DefaultSlot);
            }

            return null;
        }
    
        private ServiceCallSite TryCreateOpenGeneric(Type serviceType, CallSiteChain callSiteChain)
        {
            if (serviceType.IsConstructedGenericType
                && _descriptorLookup.TryGetValue(serviceType.GetGenericTypeDefinition(), out var descriptor))
            {
                // �����ж��������Ƿ�ṹ��һ������ʵ�������Ҹ������Ƿ��� _descriptorLookup �С�
                return TryCreateOpenGeneric(descriptor.Last, serviceType, callSiteChain, DefaultSlot);
            }

            return null;
        }

        // 
        private ServiceCallSite TryCreateEnumerable(Type serviceType, CallSiteChain callSiteChain)
        {
            if (serviceType.IsConstructedGenericType &&
                serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var itemType = serviceType.GenericTypeArguments.Single();
                callSiteChain.Add(serviceType);

                var callSites = new List<ServiceCallSite>();

                // If item type is not generic we can safely use descriptor cache
                if (!itemType.IsConstructedGenericType &&
                    _descriptorLookup.TryGetValue(itemType, out var descriptors))
                {
                    for (int i = 0; i < descriptors.Count; i++)
                    {
                        var descriptor = descriptors[i];

                        // Last service should get slot 0
                        var slot = descriptors.Count - i - 1;
                        // There may not be any open generics here
                        var callSite = TryCreateExact(descriptor, itemType, callSiteChain, slot);
                        Debug.Assert(callSite != null);

                        callSites.Add(callSite);
                    }
                }
                else
                {
                    var slot = 0;
                    // We are going in reverse so the last service in descriptor list gets slot 0
                    for (var i = _descriptors.Count - 1; i >= 0; i--)
                    {
                        var descriptor = _descriptors[i];
                        var callSite = TryCreateExact(descriptor, itemType, callSiteChain, slot) ??
                                       TryCreateOpenGeneric(descriptor, itemType, callSiteChain, slot);
                        slot++;
                        if (callSite != null)
                        {
                            callSites.Add(callSite);
                        }
                    }

                    callSites.Reverse();
                }

                return new IEnumerableCallSite(itemType, callSites.ToArray());
            }

            return null;
        }

        // TryCreateExact() �и���ע�����ķ�ʽ����ʵ���� ServiceCallSite ���Կ���ʹ�þ���ʵ������ע��ʱ ʵ���� ConstantCallSite��
        // ʹ�ù���ע��ʱ ʵ���� FactoryCallSite
        // ʹ������ע��ʱ ���� CreateConstructorCallSite������
        private ServiceCallSite TryCreateExact(ServiceDescriptor descriptor, Type serviceType, CallSiteChain callSiteChain, int slot)
        {
            if (serviceType == descriptor.ServiceType)
            {
                ServiceCallSite callSite;

                //      ���ݵ�ǰע�����������,�������ͺ�slotʵ����һ��ResultCache,
                //      ResultCache���;���һ������������λ��(�൱�ڸ���������һ��)��һ������Key
                var lifetime = new ResultCache(descriptor.Lifetime, serviceType, slot);

                //      ����ע��ʱ��ʹ�õķ�ʽ��������ͬ��ServiceCallSite,����������ServiceCallSite����
                //      ConstantCallSite    ע��ʱֱ�Ӹ��ݶ������ʵ�����������(Singleton�������ڶ���)
                //      FactoryCallSite     ע��ʱ����һ������ʵ��������
                //      ConstructorCallSite ע��ʱ���ݾ���ʵ�����ͽ���ʵ��������
                if (descriptor.ImplementationInstance != null)
                {
                    callSite = new ConstantCallSite(descriptor.ServiceType, descriptor.ImplementationInstance);
                }
                else if (descriptor.ImplementationFactory != null)
                {
                    callSite = new FactoryCallSite(lifetime, descriptor.ServiceType, descriptor.ImplementationFactory);
                }
                else if (descriptor.ImplementationType != null)
                {
                    callSite = CreateConstructorCallSite(lifetime, descriptor.ServiceType, descriptor.ImplementationType, callSiteChain);
                }
                else
                {
                    throw new InvalidOperationException("Invalid service descriptor");
                }

                return callSite;
            }

            return null;
        }

        // �÷����л����ע��ķ������͵ķ��Ͳ�������һ��ʵ�����Ͳ�����Ȼ����� CreateConstructorCallSite() ����ʵ���� CallSite
        private ServiceCallSite TryCreateOpenGeneric(ServiceDescriptor descriptor, Type serviceType, CallSiteChain callSiteChain, int slot)
        {
            if (serviceType.IsConstructedGenericType &&
                serviceType.GetGenericTypeDefinition() == descriptor.ServiceType)
            {
                Debug.Assert(descriptor.ImplementationType != null, "descriptor.ImplementationType != null");

                //  ���õ�ǰע������������������������ʵ����һ�������������
                var lifetime = new ResultCache(descriptor.Lifetime, serviceType, slot);

                //  ����ע�����ͷ��Ͳ��������������շ�������
                var closedType = descriptor.ImplementationType.MakeGenericType(serviceType.GenericTypeArguments);

                //  ����һ��ConstructorCallSite������
                return CreateConstructorCallSite(lifetime, serviceType, closedType, callSiteChain);
            }

            return null;
        }

        // ����������л�ѡ�����ʵ�������е����Ź���������ʵ����һ��ConstructorCallSite����
        private ServiceCallSite CreateConstructorCallSite(ResultCache lifetime, Type serviceType, Type implementationType,
            CallSiteChain callSiteChain)
        {
            callSiteChain.Add(serviceType, implementationType);

            // ��ȡʵ�����͵����й���������
            // Ȼ��ѡ�������ŵĹ����������� ConstructorCallSite
            var constructors = implementationType.GetTypeInfo()
                .DeclaredConstructors
                .Where(constructor => constructor.IsPublic)
                .ToArray();

            ServiceCallSite[] parameterCallSites = null;

            if (constructors.Length == 0)
            {
                // û�й�����������ֱ���׳��쳣��
                throw new InvalidOperationException(Resources.FormatNoConstructorMatch(implementationType));
            }
            else if (constructors.Length == 1)
            {
                // �����ǰ������Ϊ1�������жϹ������Ƿ���ڲ����������в�������ʵ����������ָ���� ServiceCallSite��
                var constructor = constructors[0];
                // ��ȡ��ǰ�����������в��������Բ���һһ���д��� ServiceCallSite �ݹ����
                var parameters = constructor.GetParameters();
                if (parameters.Length == 0)
                {
                    // �������������Ϊ0������ֱ��Ϊ�÷���ʵ�����ʹ��� ConstructorCallSite
                    return new ConstructorCallSite(lifetime, serviceType, constructor);
                }

                // ������ǰ�����������в����� CallSite
                // �����δ֪�Ĳ�����_descriptorLookup δ��¼�Ĳ������� ��ֱ���׳��쳣
                parameterCallSites = CreateArgumentCallSites(
                    serviceType,
                    implementationType,
                    callSiteChain,
                    parameters,
                    throwIfCallSiteNotFound: true);

                return new ConstructorCallSite(lifetime, serviceType, constructor, parameterCallSites);
            }

            // ���ݹ����������ĳ��Ƚ�������������ж����й��������Ƿ����δ֪����
            Array.Sort(constructors,
                (a, b) => b.GetParameters().Length.CompareTo(a.GetParameters().Length));

            // ���Ź�����
            ConstructorInfo bestConstructor = null;
            // ���Ų������ͼ���
            HashSet<Type> bestConstructorParameterTypes = null;
            for (var i = 0; i < constructors.Length; i++)
            {
                var parameters = constructors[i].GetParameters();

                // ������ǰ�����������в����� CallSite
                // �������δ֪�Ĳ��������׳��쳣
                var currentParameterCallSites = CreateArgumentCallSites(
                    serviceType,
                    implementationType,
                    callSiteChain,
                    parameters,
                    throwIfCallSiteNotFound: false);

                if (currentParameterCallSites != null)
                {
                    // ������в����� CallSite ����ɹ����������Ź��캯������Ϊ�գ��򽫵�ǰ����������Ϊ���Ź�������
                    if (bestConstructor == null)
                    {
                        bestConstructor = constructors[i];
                        parameterCallSites = currentParameterCallSites;
                    }
                    else
                    {
                        if (bestConstructorParameterTypes == null)
                        {
                            // ������Ų������ͼ���Ϊ�գ������Ź������Ĳ����������ϡ�
                            bestConstructorParameterTypes = new HashSet<Type>(
                                bestConstructor.GetParameters().Select(p => p.ParameterType));
                        }

                        if (!bestConstructorParameterTypes.IsSupersetOf(parameters.Select(p => p.ParameterType)))
                        {
                            // ������Ų������ͼ��ϲ�Ϊ��ǰ���캯���Ĳ������ϵ��Լ������׳��쳣
                            var message = string.Join(
                                Environment.NewLine,
                                Resources.FormatAmbiguousConstructorException(implementationType),
                                bestConstructor,
                                constructors[i]);
                            throw new InvalidOperationException(message);
                        }
                    }
                }
            }

            if (bestConstructor == null)
            {
                // ���Ϊ�ҵ����Ź��캯�������׳��쳣
                throw new InvalidOperationException(
                    Resources.FormatUnableToActivateTypeException(implementationType));
            }
            else
            {
                // ʵ����һ�� ConstructorCallSite ���󲢷��ء�
                Debug.Assert(parameterCallSites != null);
                return new ConstructorCallSite(lifetime, serviceType, bestConstructor, parameterCallSites);
            }
        }

        // �÷����ݹ���� GetCallSite() ��ȡÿһ��������Ӧ�� CallSite���ڷ����п��Կ������ GetSiteCall() ��δ��ȡ����Ӧ��
        private ServiceCallSite[] CreateArgumentCallSites(
            Type serviceType,
            Type implementationType,
            CallSiteChain callSiteChain,
            ParameterInfo[] parameters,
            bool throwIfCallSiteNotFound)
        {
            var parameterCallSites = new ServiceCallSite[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                // �ݹ���ã���ȡָ�������� CallSite
                var callSite = GetCallSite(parameters[index].ParameterType, callSiteChain);

                if (callSite == null && ParameterDefaultValue.TryGetDefaultValue(parameters[index], out var defaultValue))
                {
                    callSite = new ConstantCallSite(serviceType, defaultValue);
                }

                if (callSite == null)
                {
                    if (throwIfCallSiteNotFound)
                    {
                        throw new InvalidOperationException(Resources.FormatCannotResolveService(
                            parameters[index].ParameterType,
                            implementationType));
                    }

                    return null;
                }

                parameterCallSites[index] = callSite;
            }

            return parameterCallSites;
        }


        public void Add(Type type, ServiceCallSite serviceCallSite)
        {
            _callSiteCache[type] = serviceCallSite;
        }

        private struct ServiceDescriptorCacheItem
        {
            private ServiceDescriptor _item;
            private List<ServiceDescriptor> _items;

            public ServiceDescriptor Last
            {
                get
                {
                    if (_items != null && _items.Count > 0)
                    {
                        return _items[_items.Count - 1];
                    }

                    Debug.Assert(_item != null);
                    return _item;
                }
            }

            public int Count
            {
                get
                {
                    if (_item == null)
                    {
                        Debug.Assert(_items == null);
                        return 0;
                    }

                    return 1 + (_items?.Count ?? 0);
                }
            }

            public ServiceDescriptor this[int index]
            {
                get
                {
                    if (index >= Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    if (index == 0)
                    {
                        return _item;
                    }

                    return _items[index - 1];
                }
            }

            public ServiceDescriptorCacheItem Add(ServiceDescriptor descriptor)
            {
                var newCacheItem = new ServiceDescriptorCacheItem();
                if (_item == null)
                {
                    Debug.Assert(_items == null);
                    newCacheItem._item = descriptor;
                }
                else
                {
                    newCacheItem._item = _item;
                    newCacheItem._items = _items ?? new List<ServiceDescriptor>();
                    newCacheItem._items.Add(descriptor);
                }
                return newCacheItem;
            }
        }
    }
}
