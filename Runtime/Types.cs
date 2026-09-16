using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.Profiling;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HF.Types
{
    public static class Types
    {
        [AutoStaticsCleanup]
        private static List<Assembly> s_assemblies;
        [AutoStaticsCleanup]
        private static List<TypeInfo> s_types;
        [AutoStaticsCleanup]
        private static List<MethodInfo> s_methods;
        [AutoStaticsCleanup]
        private static List<PropertyInfo> s_properties;
        [AutoStaticsCleanup]
        private static List<FieldInfo> s_fields;

        [AutoStaticsCleanup]
        private static Dictionary<Type, List<MethodInfo>> s_methodsWithAttribute;

        [AutoStaticsCleanup]
        private static CancellationTokenSource s_cancellationTokenSource;
        [AutoStaticsCleanup]
        private static Task s_initTask;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Init()
        {
            InitSetup();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
#endif
        }

        private static void InitSetup()
        {
            s_cancellationTokenSource = new CancellationTokenSource();
            s_initTask = Task.Run(Refresh, s_cancellationTokenSource.Token);
        }

        private static void Cleanup()
        {
            try
            {
                s_cancellationTokenSource.Cancel();
                if (s_initTask != null)
                {
                    Task.WaitAny(new[] { s_initTask }, TimeSpan.FromMilliseconds(250));
                }
            }
            catch (AggregateException) { } // Intentionally swallow
            finally
            {
                s_cancellationTokenSource?.Dispose();
            }
        }

        private static void Refresh()
        {
            s_assemblies = new List<Assembly>();
            s_types = new List<TypeInfo>();
            s_methods = new List<MethodInfo>();

            s_assemblies = GetAssemblies();
            s_types = GetTypes(s_assemblies);
            s_methods = GetMethods(s_types);

            s_methodsWithAttribute = new Dictionary<Type, List<MethodInfo>>();
        }

        private static void EnsureInitialized()
        {
            s_initTask.Wait();
        }

        private static List<Assembly> GetAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies().ToList();
        }

        private static List<MethodInfo> GetMethods(IEnumerable<TypeInfo> types)
        {
            return types.SelectMany(x => x.DeclaredMethods).ToList();
        }

        private static List<TypeInfo> GetTypes(IEnumerable<Assembly> assemblies)
        {
            return assemblies.SelectMany(x => x.DefinedTypes).ToList();
        }

#if UNITY_EDITOR
        private static void PlaymodeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode)
            {
                return;
            }

            Refresh();
        }
#endif

        private static readonly ProfilerMarker s_getMethodsWithAttributeMarker = new(nameof(GetMethodsWithAttribute));

        public static IEnumerable<MethodInfo> GetMethodsWithAttribute<T>() where T : Attribute
        {
            EnsureInitialized();
            using (s_getMethodsWithAttributeMarker.Auto())
            {
                var type = typeof(T);
                if (s_methodsWithAttribute.TryGetValue(type, out var methods))
                {
                    return methods;
                }

                methods = new List<MethodInfo>();
                foreach (var method in s_methods)
                {
                    var attribute = method.GetCustomAttributes<T>(true);
                    if (attribute.Any())
                    {
                        methods.Add(method);
                    }
                }

                s_methodsWithAttribute.Add(type, methods);
                return methods;
            }
        }
    }
}
