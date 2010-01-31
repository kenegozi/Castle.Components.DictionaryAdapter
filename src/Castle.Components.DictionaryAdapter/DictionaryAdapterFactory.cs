// Copyright 2004-2009 Castle Project - http://www.castleproject.org/
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Castle.Components.DictionaryAdapter
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Collections.Specialized;
	using System.ComponentModel;
	using System.Linq;
	using System.Reflection;
	using System.Reflection.Emit;
	using System.Text;
	using System.Threading;

	/// <summary>
	/// Uses Reflection.Emit to expose the properties of a dictionary
	/// through a dynamic implementation of a typed interface.
	/// </summary>
	public class DictionaryAdapterFactory : IDictionaryAdapterFactory
	{
		private readonly IDictionary<Assembly, string> assembliesNames;

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="DictionaryAdapterFactory"/> class.
		/// </summary>
		public DictionaryAdapterFactory()
		{
			assembliesNames = new Dictionary<Assembly, string>();
		}

		#endregion

		#region IDictionaryAdapterFactory

		/// <summary>
		/// Gets a typed adapter bound to the <see cref="IDictionary"/>.
		/// </summary>
		/// <typeparam name="T">The typed interface.</typeparam>
		/// <param name="dictionary">The underlying source of properties.</param>
		/// <returns>An implementation of the typed interface bound to the dictionary.</returns>
		/// <remarks>
		/// The type represented by T must be an interface with properties.
		/// </remarks>
		public T GetAdapter<T>(IDictionary dictionary)
		{
			return (T) GetAdapter(typeof(T), dictionary);
		}

		/// <summary>
		/// Gets a typed adapter bound to the <see cref="IDictionary"/>.
		/// </summary>
		/// <param name="type">The typed interface.</param>
		/// <param name="dictionary">The underlying source of properties.</param>
		/// <returns>An implementation of the typed interface bound to the dictionary.</returns>
		/// <remarks>
		/// The type represented by T must be an interface with properties.
		/// </remarks>
		public object GetAdapter(Type type, IDictionary dictionary)
		{
			return InternalGetAdapter(type, dictionary, null);
		}

		/// <summary>
		/// Gets a typed adapter bound to the <see cref="IDictionary"/>.
		/// </summary>
		/// <param name="type">The typed interface.</param>
		/// <param name="dictionary">The underlying source of properties.</param>
		/// <param name="descriptor">The property descriptor.</param>
		/// <returns>An implementation of the typed interface bound to the dictionary.</returns>
		/// <remarks>
		/// The type represented by T must be an interface with properties.
		/// </remarks>
		public object GetAdapter(Type type, IDictionary dictionary, PropertyDescriptor descriptor)
		{
			return InternalGetAdapter(type, dictionary, descriptor);
		}

		/// <summary>
		/// Gets a typed adapter bound to the <see cref="NameValueCollection"/>.
		/// </summary>
		/// <typeparam name="T">The typed interface.</typeparam>
		/// <param name="nameValues">The underlying source of properties.</param>
		/// <returns>An implementation of the typed interface bound to the namedValues.</returns>
		/// <remarks>
		/// The type represented by T must be an interface with properties.
		/// </remarks>
		public T GetAdapter<T>(NameValueCollection nameValues)
		{
			return GetAdapter<T>(new NameValueCollectionAdapter(nameValues));
		}

		/// <summary>
		/// Gets a typed adapter bound to the <see cref="NameValueCollection"/>.
		/// </summary>
		/// <param name="type">The typed interface.</param>
		/// <param name="nameValues">The underlying source of properties.</param>
		/// <returns>An implementation of the typed interface bound to the namedValues.</returns>
		/// <remarks>
		/// The type represented by T must be an interface with properties.
		/// </remarks>
		public object GetAdapter(Type type, NameValueCollection nameValues)
		{
			return GetAdapter(type, new NameValueCollectionAdapter(nameValues));
		}

		#endregion

		readonly static Dictionary<Type, Type> _interfaceToAdapter = new Dictionary<Type, Type>();
		readonly static object _typesDictionaryLocker = new object();
		private object InternalGetAdapter(Type type, IDictionary dictionary, PropertyDescriptor descriptor)
		{
			if (!type.IsInterface)
			{
				throw new ArgumentException("Only interfaces can be adapted");
			}

			Type adapterType;
			if (_interfaceToAdapter.TryGetValue(type, out adapterType) == false)
			{
				lock (_typesDictionaryLocker)
				{
					if (_interfaceToAdapter.TryGetValue(type, out adapterType) == false)
					{
						var appDomain = Thread.GetDomain();
						var adapterAssemblyName = GetAdapterAssemblyName(type);
						var typeBuilder = CreateTypeBuilder(type, appDomain, adapterAssemblyName);
						var adapterAssembly = CreateAdapterAssembly(type, typeBuilder, descriptor);
						adapterType = CreateAdapterType(type, adapterAssembly);
						_interfaceToAdapter[type] = adapterType;
					}
				}
			}
			return CreateAdapterInstance(dictionary, descriptor, adapterType);
		}

		#region Dynamic Type Generation
		private static TypeBuilder CreateTypeBuilder(Type type, AppDomain appDomain, String adapterAssemblyName)
		{
			var assemblyName = new AssemblyName(adapterAssemblyName);
			var assemblyBuilder = appDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
			var moduleBuilder = assemblyBuilder.DefineDynamicModule(adapterAssemblyName);
			return CreateAdapterType(type, moduleBuilder);
		}

		private static TypeBuilder CreateAdapterType(Type type, ModuleBuilder moduleBuilder)
		{
			var typeBuilder = moduleBuilder.DefineType(GetAdapterFullTypeName(type),
				TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit);
			typeBuilder.AddInterfaceImplementation(type);
			typeBuilder.SetParent(typeof(DictionaryAdapterBase));

			Type[] attribCtorParams = new[] { typeof(Type) };
			var attribCtorInfo = typeof(DictionaryAdapterAttribute).GetConstructor(attribCtorParams);
			var attribBuilder = new CustomAttributeBuilder(attribCtorInfo, new[] { type });
			typeBuilder.SetCustomAttribute(attribBuilder);

			return typeBuilder;
		}

		private Assembly CreateAdapterAssembly(Type type, TypeBuilder typeBuilder, PropertyDescriptor descriptor)
		{
			var binding = FieldAttributes.Private | FieldAttributes.Static;
			var metaField = typeBuilder.DefineField("__meta", typeof(DictionaryAdapterMeta), binding);

			CreateAdapterConstructor(typeBuilder);

			object[] behaviors;
			IDictionaryInitializer[] initializers;
			var propertyMap = GetPropertyDescriptors(type, descriptor, out initializers, out behaviors);

			CreateMetaProperty(typeBuilder, AdapterGetMeta, metaField);

			foreach (var property in propertyMap)
			{
				CreateAdapterProperty(typeBuilder, property.Value);
			}

			var adapterType = typeBuilder.CreateType();
			var metaBindings = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.SetField;

			var meta = new DictionaryAdapterMeta(type, initializers, behaviors, propertyMap);
			adapterType.InvokeMember("__meta", metaBindings, null, null, new[] { meta });

			return typeBuilder.Assembly;
		}

		#endregion

		#region CreateAdapterConstructor

		private static void CreateAdapterConstructor(TypeBuilder typeBuilder)
		{
			var constructorBuilder = typeBuilder.DefineConstructor(
				MethodAttributes.Public | MethodAttributes.HideBySig, CallingConventions.Standard, 
				new [] { typeof(DictionaryAdapterInstance) }
				);

			var ilGenerator = constructorBuilder.GetILGenerator();

			var baseType = typeof(DictionaryAdapterBase);
			var baseConstructorInfo = baseType.GetConstructors()[0];

			ilGenerator.Emit(OpCodes.Ldarg_0);
			ilGenerator.Emit(OpCodes.Ldarg_1);
			ilGenerator.Emit(OpCodes.Call, baseConstructorInfo);
			ilGenerator.Emit(OpCodes.Ret);
		}

		#endregion

		#region CreateDictionaryAdapterMeta

		private static void CreateMetaProperty(TypeBuilder typeBuilder, PropertyInfo prop, FieldInfo field)
		{
			var propAttribs = MethodAttributes.Public    | MethodAttributes.SpecialName |
							  MethodAttributes.HideBySig | MethodAttributes.ReuseSlot |
							  MethodAttributes.Virtual   | MethodAttributes.Final;

			var getMethodBuilder = typeBuilder.DefineMethod("get_" + prop.Name,
														    propAttribs, prop.PropertyType, null);

			var getILGenerator = getMethodBuilder.GetILGenerator();
			if (field.IsStatic)
			{
				getILGenerator.Emit(OpCodes.Ldsfld, field);
			}
			else
			{
				getILGenerator.Emit(OpCodes.Ldarg_0);
				getILGenerator.Emit(OpCodes.Ldfld, field);
			}
			getILGenerator.Emit(OpCodes.Ret);

			typeBuilder.DefineMethodOverride(getMethodBuilder, prop.GetGetMethod());
		}

		#endregion

		#region CreateAdapterProperty

		private static void CreateAdapterProperty(TypeBuilder typeBuilder, PropertyDescriptor descriptor)
		{
			var property = descriptor.Property;
			var propertyBuilder = typeBuilder.DefineProperty(property.Name, property.Attributes, property.PropertyType, null);

			var propAttribs = MethodAttributes.Public | MethodAttributes.SpecialName |
							  MethodAttributes.HideBySig | MethodAttributes.Virtual;

			if (property.CanRead)
			{
				CreatePropertyGetMethod(typeBuilder, propertyBuilder, descriptor, propAttribs);
			}

			if (property.CanWrite)
			{
				CreatePropertySetMethod(typeBuilder, propertyBuilder, descriptor, propAttribs);
			}
		}

		private static void PreparePropertyMethod(PropertyDescriptor descriptor, ILGenerator propILGenerator)
		{
			propILGenerator.DeclareLocal(typeof(String));
			propILGenerator.DeclareLocal(typeof(object));

			// key = propertyInfo.Name
			propILGenerator.Emit(OpCodes.Ldstr, descriptor.PropertyName);
			propILGenerator.Emit(OpCodes.Stloc_0);
		}

		#endregion

		#region CreatePropertyGetMethod

		private static void CreatePropertyGetMethod(TypeBuilder typeBuilder, PropertyBuilder propertyBuilder, 
			PropertyDescriptor descriptor, MethodAttributes propAttribs)
		{
			var getMethodBuilder = typeBuilder.DefineMethod(
				"get_" + descriptor.PropertyName, propAttribs, descriptor.PropertyType, null);

			var getILGenerator = getMethodBuilder.GetILGenerator();

			var returnDefault = getILGenerator.DefineLabel();
			var storeResult = getILGenerator.DefineLabel();
			var loadResult = getILGenerator.DefineLabel();

			PreparePropertyMethod(descriptor, getILGenerator);

			var result = getILGenerator.DeclareLocal(descriptor.PropertyType);

			// value = GetProperty(key, value)
			getILGenerator.Emit(OpCodes.Ldarg_0);
			getILGenerator.Emit(OpCodes.Ldloc_0);
			getILGenerator.Emit(OpCodes.Callvirt, AdapterGetProperty);
			getILGenerator.Emit(OpCodes.Stloc_1);

			// if (value == null) return null
			getILGenerator.Emit(OpCodes.Ldloc_1);
			getILGenerator.Emit(OpCodes.Brfalse_S, returnDefault);

			// return (propertyInfo.PropertyType) value
			getILGenerator.Emit(OpCodes.Ldloc_1);
			getILGenerator.Emit(OpCodes.Unbox_Any, descriptor.PropertyType);
			getILGenerator.Emit(OpCodes.Br_S, storeResult);

			getILGenerator.MarkLabel(returnDefault);
			getILGenerator.Emit(OpCodes.Ldloca_S, result);
			getILGenerator.Emit(OpCodes.Initobj, descriptor.PropertyType);
			getILGenerator.Emit(OpCodes.Br_S, loadResult);

			getILGenerator.MarkLabel(storeResult);
			getILGenerator.Emit(OpCodes.Stloc_S, result);

			getILGenerator.MarkLabel(loadResult);
			getILGenerator.Emit(OpCodes.Ldloc_S, result);

			getILGenerator.Emit(OpCodes.Ret);

			propertyBuilder.SetGetMethod(getMethodBuilder);
		}

		#endregion

		#region CreatePropertySetMethod

		private static void CreatePropertySetMethod(TypeBuilder typeBuilder, PropertyBuilder propertyBuilder,
			PropertyDescriptor descriptor, MethodAttributes propAttribs)
		{
			var setMethodBuilder = typeBuilder.DefineMethod(
				"set_" + descriptor.PropertyName, propAttribs, null, new[] {descriptor.PropertyType});

			var setILGenerator = setMethodBuilder.GetILGenerator();
			PreparePropertyMethod(descriptor, setILGenerator);

			setILGenerator.Emit(OpCodes.Ldarg_1);
			if (descriptor.PropertyType.IsValueType)
			{
				setILGenerator.Emit(OpCodes.Box, descriptor.PropertyType);
			}
			setILGenerator.Emit(OpCodes.Stloc_1);

			// ignore = SetProperty(key, ref value)
			setILGenerator.Emit(OpCodes.Ldarg_0);
			setILGenerator.Emit(OpCodes.Ldloc_0);
			setILGenerator.Emit(OpCodes.Ldloca_S, 1);
			setILGenerator.Emit(OpCodes.Callvirt, AdapterSetProperty);
			setILGenerator.Emit(OpCodes.Pop);
			setILGenerator.Emit(OpCodes.Ret);

			propertyBuilder.SetSetMethod(setMethodBuilder);
		}

		#endregion

		#region Property Descriptors

		private static Dictionary<String, PropertyDescriptor> GetPropertyDescriptors(
			Type type, PropertyDescriptor descriptor, out IDictionaryInitializer[] typeInitializers, 
			out object[] typeBehaviors)
		{
			var propertyMap = new Dictionary<String, PropertyDescriptor>();

			typeBehaviors = GetInterfaceBehaviors<object>(type).ToArray();
			var initializers = GetOrderedBehaviors<IDictionaryInitializer>(typeBehaviors);
			if (descriptor is DictionaryDescriptor)
			{
				var dictionaryDescriptor = (DictionaryDescriptor)descriptor;
				if (dictionaryDescriptor.Initializers != null)
				{
					initializers = initializers.Union(dictionaryDescriptor.Initializers.OrderBy(i => i.ExecutionOrder));
				}
			}
			typeInitializers = initializers.ToArray();

			var typeGetters = GetOrderedBehaviors<IDictionaryPropertyGetter>(typeBehaviors);
			var typeSetters = GetOrderedBehaviors<IDictionaryPropertySetter>(typeBehaviors);

			var defaultFetch = (from b in typeBehaviors.OfType<FetchAttribute>() select b.Fetch).FirstOrDefault();

			RecursivelyDiscoverProperties(type, property =>
			{
				var propertyBehaviors = GetPropertyBehaviors<object>(property).ToArray();
				var propertyDescriptor = new PropertyDescriptor(property, propertyBehaviors);

				var descriptorInitializers = GetOrderedBehaviors<IPropertyDescriptorInitializer>(propertyBehaviors);
				foreach (var descriptorInitializer in descriptorInitializers)
				{
					descriptorInitializer.Initialize(propertyDescriptor, propertyBehaviors);	
				}

				propertyDescriptor.AddKeyBuilders(GetOrderedBehaviors<IDictionaryKeyBuilder>(propertyBehaviors));
				propertyDescriptor.AddKeyBuilders(GetInterfaceBehaviors<IDictionaryKeyBuilder>(property.ReflectedType)
					.OrderBy(b => b.ExecutionOrder));

				propertyDescriptor.AddGetters(GetOrderedBehaviors<IDictionaryPropertyGetter>(propertyBehaviors));
				propertyDescriptor.AddGetters(typeGetters);
				AddDefaultGetter(propertyDescriptor);

				propertyDescriptor.AddSetters(GetOrderedBehaviors<IDictionaryPropertySetter>(propertyBehaviors));
				propertyDescriptor.AddSetters(typeSetters);

				bool? propertyFetch = (from b in propertyBehaviors.OfType<FetchAttribute>() select b.Fetch).FirstOrDefault();
				propertyDescriptor.Fetch = propertyFetch.GetValueOrDefault(defaultFetch);

				PropertyDescriptor existingDescriptor;
				if (propertyMap.TryGetValue(property.Name, out existingDescriptor))
				{
					var existingProperty = existingDescriptor.Property;
					if (existingProperty.PropertyType == property.PropertyType)
					{
						if (property.CanRead && property.CanWrite)
						{
							propertyMap[property.Name] = propertyDescriptor;
						}
						return;
					}
				}
	
				propertyMap.Add(property.Name, propertyDescriptor);
			});

			return propertyMap;
		}

		private static IEnumerable<T> GetInterfaceBehaviors<T>(Type type) where T : class
		{
			return AttributesUtil.GetTypeAttributes<T>(type);
		}

		private static IEnumerable<T> GetPropertyBehaviors<T>(MemberInfo member) where T : class
		{
			return AttributesUtil.GetAttributes<T>(member);
		}

		private static IEnumerable<T> GetOrderedBehaviors<T>(IEnumerable<object> behaviors) 
			where T : IDictionaryBehavior
		{
			return behaviors.OfType<T>().OrderBy(b => b.ExecutionOrder);
		}

		private static void RecursivelyDiscoverProperties(Type currentType, Action<PropertyInfo> onProperty)
		{
			var types = new List<Type>();
			types.Add(currentType);
			types.AddRange(currentType.GetInterfaces());
			var publicBindings = BindingFlags.Public | BindingFlags.Instance;

			foreach (Type type in types)
			{
				if (!InfrastructureTypes.Contains(type))
				{
					foreach (var property in type.GetProperties(publicBindings))
					{
						onProperty(property);
					}
				}
			}
		}

		private static void AddDefaultGetter(PropertyDescriptor descriptor)
		{
			if (descriptor.TypeConverter != null)
			{
				descriptor.AddGetter(new DefaultPropertyGetter(descriptor.TypeConverter));
			}
		}

		private static readonly HashSet<Type> InfrastructureTypes = new HashSet<Type>()
		{
			typeof(IEditableObject), typeof(IDictionaryEdit), typeof(IChangeTracking),
			typeof(IRevertibleChangeTracking), typeof(IDictionaryNotify), typeof(IDataErrorInfo), 
			typeof(IDictionaryValidate), typeof(IDictionaryAdapter)
		};

		#endregion

		#region Assembly Support 

		private string GetAdapterAssemblyName(Type type)
		{
			return string.Concat(GetAssemblyName( type.Assembly ), ".",
				GetSafeTypeFullName(type), ".DictionaryAdapter" );
		}

		private string GetAssemblyName(Assembly assembly)
		{
			string assemblyName;
			if (!assembliesNames.TryGetValue(assembly, out assemblyName))
			{
				assemblyName = assembly.GetName().Name;
				assembliesNames[assembly] = assemblyName;
			}

			return assemblyName;
		}

		private static String GetAdapterFullTypeName(Type type)
		{
			return type.Namespace + "." + GetAdapterTypeName(type);
		}

		private static String GetAdapterTypeName(Type type)
		{
			return GetSafeTypeName(type).Substring(1) + "DictionaryAdapter";
		}

		public static string GetSafeTypeFullName(Type type)
		{
			if (type.IsGenericTypeDefinition)
			{
				return type.FullName.Replace("`", "_");
			}

			if (type.IsGenericType)
			{
				var name = new StringBuilder();
				if (!string.IsNullOrEmpty(type.Namespace))
				{
					name.Append(type.Namespace).Append(".");
				}

				AppendGenericTypeName(type, name);
				return name.ToString();
			}

			return type.FullName;
		}

		public static string GetSafeTypeName(Type type)
		{
			if (type.IsGenericTypeDefinition)
			{
				return type.Name.Replace("`", "_");
			}

			if (type.IsGenericType)
			{
				var name = new StringBuilder();
				AppendGenericTypeName(type, name);
				return name.ToString();
			}

			return type.Name;
		}

		private static void AppendGenericTypeName(Type type, StringBuilder sb)
		{
			// Replace back tick preceding parameter count with _ List`1 => List_1
			sb.Append(type.Name.Replace("`", "_"));

			// Append safe full name of each type argument, separated by _
			foreach (var argument in type.GetGenericArguments())
			{
				sb.Append("_").Append(GetSafeTypeFullName(argument).Replace(".", "_"));
			}
		}

		private Type CreateAdapterType(Type type, Assembly assembly)
		{
			var adapterFullTypeName = GetAdapterFullTypeName(type);
			return assembly.GetType(adapterFullTypeName, true);
		}
		private object CreateAdapterInstance(IDictionary dictionary, PropertyDescriptor descriptor, Type adapterType)
		{
            var instance = new DictionaryAdapterInstance(dictionary, descriptor, this);
			return Activator.CreateInstance(adapterType, instance);
		}

		#endregion

		#region Reflection Cache

		private static readonly PropertyInfo AdapterGetMeta =
			typeof(IDictionaryAdapter).GetProperty("Meta");

		private static readonly MethodInfo AdapterGetProperty =
			typeof(IDictionaryAdapter).GetMethod("GetProperty");

		private static readonly MethodInfo AdapterSetProperty =
			typeof(IDictionaryAdapter).GetMethod("SetProperty");

		#endregion
	}
}
