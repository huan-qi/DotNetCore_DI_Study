using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    //summary
    //这个类型是一个创建或获取服务实例的类型，这个类型的参数分别为 RuntimeResolverContext 类型和实例对象类型 Object
    //RuntimeResolverContext 是一个封装了 ServiceProviderEngineScope 的结构体。这个类型中具有一个 ServiceProviderEngineScope 类型属性 
    //和一个 RuntimeResolverLock 枚举类型属性，这个枚举类型在实例化对象时当做了锁使用。
    
    //在 CallSiteRuntimeResolver 类型中有两类方法。
    //1. 根据注册服务的生命周期进行访问服务实例对象。
    //2. 根据 ServiceCallSite 的设置类型进行访问服务实例对象。
    //这两类方法都在 CallSiteVisitor<TArgument, TResult> 基类中
    internal sealed class CallSiteRuntimeResolver : CallSiteVisitor<RuntimeResolverContext, object>
    {
        public object Resolve(ServiceCallSite callSite, ServiceProviderEngineScope scope)
        {
            return VisitCallSite(callSite, new RuntimeResolverContext
            {
                Scope = scope
            });
        }

        // 这个方法是访问 Transient 生命周期的方法，可以看到这个方法直接调用 VisitCallSiteMain() 进行获取实例对象，然后调用 CaptureDisposable() 将次对象尝试缓存到 ServiceProviderEngineScope 容器的 _disposables 集合中
        protected override object VisitDisposeCache(ServiceCallSite transientCallSite, RuntimeResolverContext context)
        {
            return context.Scope.CaptureDisposable(VisitCallSiteMain(transientCallSite, context));
        }

        protected override object VisitConstructor(ConstructorCallSite constructorCallSite, RuntimeResolverContext context)
        {
            object[] parameterValues;
            if (constructorCallSite.ParameterCallSites.Length == 0)
            {
                parameterValues = Array.Empty<object>();
            }
            else
            {
                parameterValues = new object[constructorCallSite.ParameterCallSites.Length];
                for (var index = 0; index < parameterValues.Length; index++)
                {
                    parameterValues[index] = VisitCallSite(constructorCallSite.ParameterCallSites[index], context);
                }
            }

            try
            {
                return constructorCallSite.ConstructorInfo.Invoke(parameterValues);
            }
            catch (Exception ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                // The above line will always throw, but the compiler requires we throw explicitly.
                throw;
            }
        }

        // 这个方法是访问Root声明周期的方法，可以看到这个方法调用了一个 VisitCache()，这个方法一共接收四个参数。
        // 第一个、第二个分别是当前方法的参数。第三个参数代表容器对象，容器使用的 ServiceProviderEngine 实例中的 Root 属性，这个容器代表了顶级容器。（也就是root生命周期的本质，使用定期容器进行创建/获取实例）
        // 第四个参数是锁，此方法使用的是 RuntimeResolveLock.Root 锁。
        protected override object VisitRootCache(ServiceCallSite singletonCallSite, RuntimeResolverContext context)
        {
            return VisitCache(singletonCallSite, context, context.Scope.Engine.Root, RuntimeResolverLock.Root);
        }

        // 这个方法是访问Scoped生命周期的方法，和上面方法相似，也是调用了VisitCache()，但是不同的是锁不同，这个锁是根据当前容器来决定，如果当前容器为顶级容器为顶级容器，就是用 Root 锁，所以不为顶级容器，则使用 Scope 锁。
        protected override object VisitScopeCache(ServiceCallSite singletonCallSite, RuntimeResolverContext context)
        {
            // 如果当前容器为跟容器，则将其锁转换为Root，否则为Scope
            var requiredScope = context.Scope == context.Scope.Engine.Root ?
                RuntimeResolverLock.Root :
                RuntimeResolverLock.Scope;

            return VisitCache(singletonCallSite, context, context.Scope, requiredScope);
        }


        // 这个方法是使用指定的容器进行实例化并缓存服务实例对象，在下面代码中可以看到，代码中根据 RuntimeResolveContext 实例的枚举值与RuntimeResolverContext.AcquiredLocks 进行对比，如果不相同，则进行加锁。
        // 然后进行获取实例服务对象。如果已缓存则直接获取，没有缓存则调用 VisitCallSiteMain() 获取实例并缓存。
        private object VisitCache(ServiceCallSite scopedCallSite, RuntimeResolverContext context, ServiceProviderEngineScope serviceProviderEngine, RuntimeResolverLock lockType)
        {
            bool lockTaken = false;
            var resolvedServices = serviceProviderEngine.ResolvedServices;

            // Taking locks only once allows us to fork resolution process
            // on another thread without causing the deadlock because we
            // always know that we are going to wait the other thread to finish before
            // releasing the lock
            if ((context.AcquiredLocks & lockType) == 0)
            {
                Monitor.Enter(resolvedServices, ref lockTaken);
            }

            try
            {
                if (!resolvedServices.TryGetValue(scopedCallSite.Cache.Key, out var resolved))
                {
                    resolved = VisitCallSiteMain(scopedCallSite, new RuntimeResolverContext
                    {
                        Scope = serviceProviderEngine,
                        AcquiredLocks = context.AcquiredLocks | lockType
                    });

                    serviceProviderEngine.CaptureDisposable(resolved);
                    resolvedServices.Add(scopedCallSite.Cache.Key, resolved);
                }

                return resolved;
            }
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(resolvedServices);
                }
            }
        }

        protected override object VisitConstant(ConstantCallSite constantCallSite, RuntimeResolverContext context)
        {
            return constantCallSite.DefaultValue;
        }

        protected override object VisitServiceProvider(ServiceProviderCallSite serviceProviderCallSite, RuntimeResolverContext context)
        {
            return context.Scope;
        }

        protected override object VisitServiceScopeFactory(ServiceScopeFactoryCallSite serviceScopeFactoryCallSite, RuntimeResolverContext context)
        {
            return context.Scope.Engine;
        }

        protected override object VisitIEnumerable(IEnumerableCallSite enumerableCallSite, RuntimeResolverContext context)
        {
            var array = Array.CreateInstance(
                enumerableCallSite.ItemType,
                enumerableCallSite.ServiceCallSites.Length);

            for (var index = 0; index < enumerableCallSite.ServiceCallSites.Length; index++)
            {
                var value = VisitCallSite(enumerableCallSite.ServiceCallSites[index], context);
                array.SetValue(value, index);
            }
            return array;
        }

        protected override object VisitFactory(FactoryCallSite factoryCallSite, RuntimeResolverContext context)
        {
            return factoryCallSite.Factory(context.Scope);
        }
    }

    internal struct RuntimeResolverContext
    {
        public ServiceProviderEngineScope Scope { get; set; }

        public RuntimeResolverLock AcquiredLocks { get; set; }
    }

    [Flags]
    internal enum RuntimeResolverLock
    {
        Scope = 1,
        Root = 2
    }
}