# Permixa.Caching.Redis

Optional **Redis authorization-cache** provider for [Permixa](https://github.com/AbdulHaDi-Darwish-Dev/Permixa).

## Purpose

Distributed caching of **authorization snapshots** (`IPermissionCache`) using StackExchange.Redis.

- SQL Server remains the **source of truth**
- Redis failures fail open (treat as cache miss / skip write)
- This is **not** distributed Rate Limiting

## Install

```bash
dotnet add package Permixa.AspNetCore --prerelease
dotnet add package Permixa.Caching.Redis --prerelease
```

## Registration

```csharp
using Permixa.Caching.Redis;

services.AddPermixaInfrastructure(...); // registers MemoryPermissionCache by default
services.AddPermixaRedisAuthorizationCache(o =>
{
    o.ConnectionString = configuration.GetConnectionString("Redis");
    // o.KeyPrefix = "permixa:authz:"; // default
});
```

Call after `AddPermixaInfrastructure`. The Redis registration replaces the default memory cache implementation (last registration wins).

## License

Apache-2.0 — preview package (`0.1.0-preview.1`).
