﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace StatusAggregator
{
    public interface IEntityCollectorProcessor
    {
        string Name { get; }

        Task<DateTime?> FetchSince(DateTime cursor);
    }
}
