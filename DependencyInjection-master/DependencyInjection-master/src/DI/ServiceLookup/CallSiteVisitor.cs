using System;
using System.Runtime.CompilerServices;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    // 这个类使用了访问者模式
    internal abstract class CallSiteVisitor<TArgument, TResult>
    {
        private readonly StackGuard _stackGuard;

        protected CallSiteVisitor()
        {
            _stackGuard = new StackGuard();
        }

        // 根据服务对象的生命周期进行访问服务实例。
        protected virtual TResult VisitCallSite(ServiceCallSite callSite, TArgument argument)
        {
            if (!_stackGuard.TryEnterOnCurrentStack())
            {
                return _stackGuard.RunOnEmptyStack((c, a) => VisitCallSite(c, a), callSite, argument);
            }
            // 缓存位置由 ServiceCallSite 内部的 Cache 属性的 Location 提供
            switch (callSite.Cache.Location)
            {
                case CallSiteResultCacheLocation.Root:
                    return VisitRootCache(callSite, argument);
                case CallSiteResultCacheLocation.Scope:
                    return VisitScopeCache(callSite, argument);
                case CallSiteResultCacheLocation.Dispose:
                    return VisitDisposeCache(callSite, argument);
                case CallSiteResultCacheLocation.None:
                    return VisitNoCache(callSite, argument);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // 根据其 ServiceCallSite 的 Kind 属性访问服务对象。
        protected virtual TResult VisitCallSiteMain(ServiceCallSite callSite, TArgument argument)
        {
            switch (callSite.Kind)
            {
                case CallSiteKind.Factory:
                    return VisitFactory((FactoryCallSite)callSite, argument);
                case  CallSiteKind.IEnumerable:
                    return VisitIEnumerable((IEnumerableCallSite)callSite, argument);
                case CallSiteKind.Constructor:
                    return VisitConstructor((ConstructorCallSite)callSite, argument);
                case CallSiteKind.Constant:
                    return VisitConstant((ConstantCallSite)callSite, argument);
                case CallSiteKind.ServiceProvider:
                    return VisitServiceProvider((ServiceProviderCallSite)callSite, argument);
                case CallSiteKind.ServiceScopeFactory:
                    return VisitServiceScopeFactory((ServiceScopeFactoryCallSite)callSite, argument);
                default:
                    throw new NotSupportedException($"Call site type {callSite.GetType()} is not supported");
            }
        }

        protected virtual TResult VisitNoCache(ServiceCallSite callSite, TArgument argument)
        {
            return VisitCallSiteMain(callSite, argument);
        }

        protected virtual TResult VisitDisposeCache(ServiceCallSite callSite, TArgument argument)
        {
            return VisitCallSiteMain(callSite, argument);
        }

        // 这个方法在派生类 CallSiteRuntimeResolve 中被重写
        protected virtual TResult VisitRootCache(ServiceCallSite callSite, TArgument argument)
        {
            return VisitCallSiteMain(callSite, argument);
        }

        protected virtual TResult VisitScopeCache(ServiceCallSite callSite, TArgument argument)
        {
            return VisitCallSiteMain(callSite, argument);
        }

        protected abstract TResult VisitConstructor(ConstructorCallSite constructorCallSite, TArgument argument);

        protected abstract TResult VisitConstant(ConstantCallSite constantCallSite, TArgument argument);

        protected abstract TResult VisitServiceProvider(ServiceProviderCallSite serviceProviderCallSite, TArgument argument);

        protected abstract TResult VisitServiceScopeFactory(ServiceScopeFactoryCallSite serviceScopeFactoryCallSite, TArgument argument);

        protected abstract TResult VisitIEnumerable(IEnumerableCallSite enumerableCallSite, TArgument argument);

        protected abstract TResult VisitFactory(FactoryCallSite factoryCallSite, TArgument argument);
    }
}