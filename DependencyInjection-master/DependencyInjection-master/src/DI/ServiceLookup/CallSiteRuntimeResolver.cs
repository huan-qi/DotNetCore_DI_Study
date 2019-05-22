using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    //summary
    //���������һ���������ȡ����ʵ�������ͣ�������͵Ĳ����ֱ�Ϊ RuntimeResolverContext ���ͺ�ʵ���������� Object
    //RuntimeResolverContext ��һ����װ�� ServiceProviderEngineScope �Ľṹ�塣��������о���һ�� ServiceProviderEngineScope �������� 
    //��һ�� RuntimeResolverLock ö���������ԣ����ö��������ʵ��������ʱ��������ʹ�á�
    
    //�� CallSiteRuntimeResolver �����������෽����
    //1. ����ע�������������ڽ��з��ʷ���ʵ������
    //2. ���� ServiceCallSite ���������ͽ��з��ʷ���ʵ������
    //�����෽������ CallSiteVisitor<TArgument, TResult> ������
    internal sealed class CallSiteRuntimeResolver : CallSiteVisitor<RuntimeResolverContext, object>
    {
        public object Resolve(ServiceCallSite callSite, ServiceProviderEngineScope scope)
        {
            return VisitCallSite(callSite, new RuntimeResolverContext
            {
                Scope = scope
            });
        }

        // ��������Ƿ��� Transient �������ڵķ��������Կ����������ֱ�ӵ��� VisitCallSiteMain() ���л�ȡʵ������Ȼ����� CaptureDisposable() ���ζ����Ի��浽 ServiceProviderEngineScope ������ _disposables ������
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

        // ��������Ƿ���Root�������ڵķ��������Կ����������������һ�� VisitCache()���������һ�������ĸ�������
        // ��һ�����ڶ����ֱ��ǵ�ǰ�����Ĳ�������������������������������ʹ�õ� ServiceProviderEngine ʵ���е� Root ���ԣ�������������˶�����������Ҳ����root�������ڵı��ʣ�ʹ�ö����������д���/��ȡʵ����
        // ���ĸ������������˷���ʹ�õ��� RuntimeResolveLock.Root ����
        protected override object VisitRootCache(ServiceCallSite singletonCallSite, RuntimeResolverContext context)
        {
            return VisitCache(singletonCallSite, context, context.Scope.Engine.Root, RuntimeResolverLock.Root);
        }

        // ��������Ƿ���Scoped�������ڵķ����������淽�����ƣ�Ҳ�ǵ�����VisitCache()�����ǲ�ͬ��������ͬ��������Ǹ��ݵ�ǰ�����������������ǰ����Ϊ��������Ϊ���������������� Root �������Բ�Ϊ������������ʹ�� Scope ����
        protected override object VisitScopeCache(ServiceCallSite singletonCallSite, RuntimeResolverContext context)
        {
            // �����ǰ����Ϊ��������������ת��ΪRoot������ΪScope
            var requiredScope = context.Scope == context.Scope.Engine.Root ?
                RuntimeResolverLock.Root :
                RuntimeResolverLock.Scope;

            return VisitCache(singletonCallSite, context, context.Scope, requiredScope);
        }


        // ���������ʹ��ָ������������ʵ�������������ʵ����������������п��Կ����������и��� RuntimeResolveContext ʵ����ö��ֵ��RuntimeResolverContext.AcquiredLocks ���жԱȣ��������ͬ������м�����
        // Ȼ����л�ȡʵ�������������ѻ�����ֱ�ӻ�ȡ��û�л�������� VisitCallSiteMain() ��ȡʵ�������档
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