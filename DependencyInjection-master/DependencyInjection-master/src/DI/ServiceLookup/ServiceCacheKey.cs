// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal struct ServiceCacheKey: IEquatable<ServiceCacheKey>
    {
        public static ServiceCacheKey Empty { get; } = new ServiceCacheKey(null, 0);

        /// <summary>
        /// 服务类型
        /// </summary>
        public Type Type { get; }

        /// <summary>
        /// Reverse index of the service when resolved in <code>IEnumerable&lt;Type&gt;</code> where default instance gets slot 0.
        /// For example for service collection
        ///  IService Impl1
        ///  IService Impl2
        ///  IService Impl3
        /// We would get the following cache keys:
        ///  Impl1 2
        ///  Impl2 1
        ///  Impl3 0
        /// </summary>
        /// 当服务实例类型为枚举类型的时候，slot的值是descriptors集合类型的反向索引
        /// 当服务类型为其他类型的时候，slot的值是默认值 0
        public int Slot { get; }

        public ServiceCacheKey(Type type, int slot)
        {
            Type = type;
            Slot = slot;
        }

        public bool Equals(ServiceCacheKey other)
        {
            return Type == other.Type && Slot == other.Slot;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Type.GetHashCode() * 397) ^ Slot;
            }
        }
    }
}