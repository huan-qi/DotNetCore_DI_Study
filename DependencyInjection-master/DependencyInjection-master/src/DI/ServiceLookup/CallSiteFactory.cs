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
    /// 这个类型是 ServiceCallSite 的工厂类型，内部根据 ServiceDescriptor 创建对应的 ServiceCallSite
    /// </summary>
    internal class CallSiteFactory
    {
        // 此属性是默认的Slot，默认为 0
        private const int DefaultSlot = 0;
		
        // 此属性是缓存所有的ServiceDescriptor
        private readonly List<ServiceDescriptor> _descriptors;
		
		// ServiceCallSite 的缓存集合
		// ServiceCallSite  是创建服务实例的依据，包含了服务类型，实例类型，CallSite 种类，实例对象的缓存配置
        private readonly ConcurrentDictionary<Type, ServiceCallSite> _callSiteCache = new ConcurrentDictionary<Type, ServiceCallSite>();
		
		// ServiceDescriptorCacheItem 的缓存集合 
		// 因为注册服务的时候第一没有验证实例类型的有效性（接口，抽象类等），此外我们可以针对同一个服务进行多次注册，对多次注册的服务微软又是如何确定创建的对象呢？ 
		// 针对这些问题，微软设计了一个结构体，概括了一个具体服务的所有注册的实例类型 （ServiceDescriptorCacheItem）
		// 第一次注册的元数据存储在 _item 中，后续该服务的所有元数据都存储在 _items 中，而默认总是认同最后一个元数据。
        private readonly Dictionary<Type, ServiceDescriptorCacheItem> _descriptorLookup = new Dictionary<Type, ServiceDescriptorCacheItem>();
	
        private readonly StackGuard _stackGuard;

        public CallSiteFactory(IEnumerable<ServiceDescriptor> descriptors)
        {
            _stackGuard = new StackGuard();
            _descriptors = descriptors.ToList();
            Populate(descriptors);
        }
		
        // 将我们创建的 IServiceCollection 服务集合转存到 _descriptorLookup 中。
		// 首先会进行一些列的判断，最后进行缓存
        private void Populate(IEnumerable<ServiceDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
				// 获取ServiceDescription对象中所注册的服务类型
                var serviceTypeInfo = descriptor.ServiceType.GetTypeInfo();
                if (serviceTypeInfo.IsGenericTypeDefinition)
                {
					// 获取当前服务实例类型的类型
                    var implementationTypeInfo = descriptor.ImplementationType?.GetTypeInfo();
					// 如果实例类型implementationTypeInfo类型为空，或者不是泛型类型，则抛出异常。
                    if (implementationTypeInfo == null || !implementationTypeInfo.IsGenericTypeDefinition)
                    {
                        throw new ArgumentException(
                            Resources.FormatOpenGenericServiceRequiresOpenGenericImplementation(descriptor.ServiceType),
                            nameof(descriptors));
                    }
					// 如果实例类型implementationTypeInfo类型为抽象类型，或者为接口，则抛出异常。
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
					// 如果当前服务实例类型不为泛型类
					// 并且实例类型为抽象类或接口类型，则抛出异常
                    if (implementationTypeInfo.IsGenericTypeDefinition ||
                        implementationTypeInfo.IsAbstract ||
                        implementationTypeInfo.IsInterface)
                    {
                        throw new ArgumentException(
                            Resources.FormatTypeCannotBeActivated(descriptor.ImplementationType, descriptor.ServiceType));
                    }
                }

				// 使用服务类型做为key，将此 ServiceDescriptor 缓存到 Dictionary<Type, ServiceDescriptorCacheItem> 集合中
                var cacheKey = descriptor.ServiceType;
				
				// 由于 ServiceDescriptorCacheItem 是一个结构，所以不会异常
                _descriptorLookup.TryGetValue(cacheKey, out var cacheItem);
                _descriptorLookup[cacheKey] = cacheItem.Add(descriptor);
            }
        }

		// GetCallSite() 方法，外部也是调用此方法进行获取 ServiceCallSite，如果当前 ServiceCallSite 已被缓存，则直接获取缓存中数据，
		// 如果未缓存，则创建并缓存。（调用 CreateCallSite() 进行缓存）
		// 当前函数中有一个 CallSiteChain 类型，这个类型是一个限制，应该是为了防止多线程，在创建之前进行了判断，如果已创建，则抛出异常，CallSiteChain 这个类在此就不做介绍。
        internal ServiceCallSite GetCallSite(Type serviceType, CallSiteChain callSiteChain)
        {
#if NETCOREAPP2_0
            return _callSiteCache.GetOrAdd(serviceType, (type, chain) => CreateCallSite(type, chain), callSiteChain);
#else
            return _callSiteCache.GetOrAdd(serviceType, type => CreateCallSite(type, callSiteChain));
#endif
        }

		// 在CreateCallSite() 首先调用了 CallSiteChain 实例的 CheckCircularDependency() 方法，这个方法就是如果已被创建,则抛出异常.
		// 然后分别调用TryCreateExact(),TryCreateOpenGeneric(),TryCreateEnumerable()这三个方法进行尝试实例化 ServiceCallSite 
        private ServiceCallSite CreateCallSite(Type serviceType, CallSiteChain callSiteChain)
        {
            if (!_stackGuard.TryEnterOnCurrentStack())
            {
                return _stackGuard.RunOnEmptyStack((type, chain) => CreateCallSite(type, chain), serviceType, callSiteChain);
            }

            ServiceCallSite callSite;
            try
            {
				// 检查是否已被创建,如果已创建,则抛出异常
                callSiteChain.CheckCircularDependency(serviceType);
				// 获取指定服务的实例对象方式
				//   1.首先创建普通类型的ServiceCallSite,
				//   2.创建泛型类型的ServiceCallSite
				//   3.如果服务类型是集合.那么将获取当前类型所有实现对象
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

		// TryCreateExact()方法是如果服务类型只是一个普通类型时才使用的方法,如下代码,首先判断了此类型是否存在于 _descriptorLookup 缓存中，
		// 如果不存在直接返回null,如果存在的话直接使用最后一个 ServiceDescriptor 和 DefaultSlot。
        private ServiceCallSite TryCreateExact(Type serviceType, CallSiteChain callSiteChain)
        {
            if (_descriptorLookup.TryGetValue(serviceType, out var descriptor))
            {
                // 指定 _descriptorLookup 中缓存的所有服务实例类型中的最后一个作为实例类型。
                return TryCreateExact(descriptor.Last, serviceType, callSiteChain, DefaultSlot);
            }

            return null;
        }
    
        private ServiceCallSite TryCreateOpenGeneric(Type serviceType, CallSiteChain callSiteChain)
        {
            if (serviceType.IsConstructedGenericType
                && _descriptorLookup.TryGetValue(serviceType.GetGenericTypeDefinition(), out var descriptor))
            {
                // 首先判定该类型是否会构造一个泛型实例，并且该类型是否在 _descriptorLookup 中。
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

        // TryCreateExact() 中根据注册服务的方式进行实例化 ServiceCallSite 可以看到使用具体实例对象注册时 实例化 ConstantCallSite；
        // 使用工厂注册时 实例化 FactoryCallSite
        // 使用类型注册时 调用 CreateConstructorCallSite方法。
        private ServiceCallSite TryCreateExact(ServiceDescriptor descriptor, Type serviceType, CallSiteChain callSiteChain, int slot)
        {
            if (serviceType == descriptor.ServiceType)
            {
                ServiceCallSite callSite;

                //      根据当前注册的生命周期,基类类型和slot实例化一个ResultCache,
                //      ResultCache类型具有一个最后结果缓存的位置(相当于跟生命周期一致)和一个缓存Key
                var lifetime = new ResultCache(descriptor.Lifetime, serviceType, slot);

                //      根据注册时所使用的方式来创建不同的ServiceCallSite,共具有三种ServiceCallSite子类
                //      ConstantCallSite    注册时直接根据对象进行实例化具体对象(Singleton生命周期独有)
                //      FactoryCallSite     注册时根据一个工厂实例化对象
                //      ConstructorCallSite 注册时根据具体实例类型进行实例化对象
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

        // 该方法中会根据注册的服务类型的泛型参数制造一个实现类型参数，然后调用 CreateConstructorCallSite() 进行实例化 CallSite
        private ServiceCallSite TryCreateOpenGeneric(ServiceDescriptor descriptor, Type serviceType, CallSiteChain callSiteChain, int slot)
        {
            if (serviceType.IsConstructedGenericType &&
                serviceType.GetGenericTypeDefinition() == descriptor.ServiceType)
            {
                Debug.Assert(descriptor.ImplementationType != null, "descriptor.ImplementationType != null");

                //  利用当前注册服务的声明和生命周期类型实例化一个结果缓存配置
                var lifetime = new ResultCache(descriptor.Lifetime, serviceType, slot);

                //  利用注册类型泛型参数创造派生类封闭泛型类型
                var closedType = descriptor.ImplementationType.MakeGenericType(serviceType.GenericTypeArguments);

                //  创建一个ConstructorCallSite并返回
                return CreateConstructorCallSite(lifetime, serviceType, closedType, callSiteChain);
            }

            return null;
        }

        // 在这个方法中会选择服务实例类型中的最优构造器，并实例化一个ConstructorCallSite对象。
        private ServiceCallSite CreateConstructorCallSite(ResultCache lifetime, Type serviceType, Type implementationType,
            CallSiteChain callSiteChain)
        {
            callSiteChain.Add(serviceType, implementationType);

            // 获取实例类型的所有公共构造器
            // 然后选择其最优的构造器并创建 ConstructorCallSite
            var constructors = implementationType.GetTypeInfo()
                .DeclaredConstructors
                .Where(constructor => constructor.IsPublic)
                .ToArray();

            ServiceCallSite[] parameterCallSites = null;

            if (constructors.Length == 0)
            {
                // 没有公共构造器，直接抛出异常。
                throw new InvalidOperationException(Resources.FormatNoConstructorMatch(implementationType));
            }
            else if (constructors.Length == 1)
            {
                // 如果当前构造器为1个，则判断构造器是否存在参数并将所有参数进行实例化（创建指定的 ServiceCallSite）
                var constructor = constructors[0];
                // 获取当前构造器的所有参数，并对参数一一进行创建 ServiceCallSite 递归调用
                var parameters = constructor.GetParameters();
                if (parameters.Length == 0)
                {
                    // 如果构造器参数为0个，则直接为该服务实例类型创建 ConstructorCallSite
                    return new ConstructorCallSite(lifetime, serviceType, constructor);
                }

                // 创建当前构造器的所有参数的 CallSite
                // 如果有未知的参数（_descriptorLookup 未记录的参数）， 则直接抛出异常
                parameterCallSites = CreateArgumentCallSites(
                    serviceType,
                    implementationType,
                    callSiteChain,
                    parameters,
                    throwIfCallSiteNotFound: true);

                return new ConstructorCallSite(lifetime, serviceType, constructor, parameterCallSites);
            }

            // 根据构造器参数的长度进行排序，下面会判断所有构造器中是否具有未知参数
            Array.Sort(constructors,
                (a, b) => b.GetParameters().Length.CompareTo(a.GetParameters().Length));

            // 最优构造器
            ConstructorInfo bestConstructor = null;
            // 最优参数类型集合
            HashSet<Type> bestConstructorParameterTypes = null;
            for (var i = 0; i < constructors.Length; i++)
            {
                var parameters = constructors[i].GetParameters();

                // 创建当前构造器的所有参数的 CallSite
                // 如果具有未知的参数，则不抛出异常
                var currentParameterCallSites = CreateArgumentCallSites(
                    serviceType,
                    implementationType,
                    callSiteChain,
                    parameters,
                    throwIfCallSiteNotFound: false);

                if (currentParameterCallSites != null)
                {
                    // 如果所有参数的 CallSite 构造成功，并且最优构造函数对象为空，则将当前构造器设置为最优构造器。
                    if (bestConstructor == null)
                    {
                        bestConstructor = constructors[i];
                        parameterCallSites = currentParameterCallSites;
                    }
                    else
                    {
                        if (bestConstructorParameterTypes == null)
                        {
                            // 如果最优参数类型集合为空，则将最优构造器的参数赋给集合。
                            bestConstructorParameterTypes = new HashSet<Type>(
                                bestConstructor.GetParameters().Select(p => p.ParameterType));
                        }

                        if (!bestConstructorParameterTypes.IsSupersetOf(parameters.Select(p => p.ParameterType)))
                        {
                            // 如果最优参数类型集合不为当前构造函数的参数集合的自己，则抛出异常
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
                // 如果为找到最优构造函数，则抛出异常
                throw new InvalidOperationException(
                    Resources.FormatUnableToActivateTypeException(implementationType));
            }
            else
            {
                // 实例化一个 ConstructorCallSite 对象并返回。
                Debug.Assert(parameterCallSites != null);
                return new ConstructorCallSite(lifetime, serviceType, bestConstructor, parameterCallSites);
            }
        }

        // 该方法递归调用 GetCallSite() 获取每一个参数对应的 CallSite，在方法中可以看到如果 GetSiteCall() 中未获取到对应的
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
                // 递归调用，获取指定参数的 CallSite
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
