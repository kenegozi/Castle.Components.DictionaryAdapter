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
	using System.ComponentModel;
	using System.Linq;

	public abstract partial class DictionaryAdapterBase : IDictionaryAdapter
	{
		public DictionaryAdapterBase(DictionaryAdapterInstance instance)
		{
			This = instance;

			CanEdit = typeof(IEditableObject).IsAssignableFrom(Meta.Type);
			CanNotify = typeof(INotifyPropertyChanged).IsAssignableFrom(Meta.Type);
			CanValidate = typeof(IDataErrorInfo).IsAssignableFrom(Meta.Type);

			Initialize();
		}

		public abstract DictionaryAdapterMeta Meta { get; }

		public DictionaryAdapterInstance This { get; private set; }

		public virtual object GetProperty(string propertyName)
		{
			PropertyDescriptor descriptor;
			if (Meta.Properties.TryGetValue(propertyName, out descriptor))
			{
				var propertyValue = descriptor.GetPropertyValue(this, propertyName, null, This.Descriptor);
				if (propertyValue is IEditableObject)
				{
					AddEditDependency((IEditableObject)propertyValue);
				}
				ComposeChildNotifications(descriptor, null, propertyValue);
				return propertyValue;
			}

			return null;
		}

		public object ReadProperty(PropertyDescriptor property, string key)
		{
			object propertyValue = null;
			if (!GetEditedProperty(key, out propertyValue))
			{
				propertyValue = This.Dictionary[key];
			}
			return propertyValue;
		}
        
		public T GetPropertyOfType<T>(string propertyName)
		{
			return (T)GetProperty(propertyName);
		}

		public virtual bool SetProperty(string propertyName, ref object value)
		{
			bool stored = false;

			PropertyDescriptor descriptor;
			if (Meta.Properties.TryGetValue(propertyName, out descriptor))
			{
				if (!ShouldNotify)
				{
					stored = descriptor.SetPropertyValue(this, propertyName, ref value, This.Descriptor);
					Invalidate();
					return stored;
				}

				var existingValue = GetProperty(propertyName);
				if (!NotifyPropertyChanging(descriptor, existingValue, value))
				{
					return false;
				}

				var trackPropertyChange = TrackPropertyChange(descriptor, existingValue, value);

				stored = descriptor.SetPropertyValue(this, propertyName, ref value, This.Descriptor);

				if (stored && trackPropertyChange != null)
				{
					trackPropertyChange.Notify();
				}
			}

			return stored;
		}

		public void StoreProperty(PropertyDescriptor property, string key, object value)
		{
			if (property == null || !EditProperty(property, key, value))
			{
				This.Dictionary[key] = value;
			}
		}
        
		protected void Initialize()
		{
			foreach (var initializer in Meta.Initializers)
			{
				initializer.Initialize(this, Meta.Behaviors);
			}

			foreach (var property in Meta.Properties.Values.Where(p => p.Fetch))
			{
				GetProperty(property.PropertyName);
			}
		}
	}
}
