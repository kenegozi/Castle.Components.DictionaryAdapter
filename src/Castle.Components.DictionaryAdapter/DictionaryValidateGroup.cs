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
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Linq;

	public class DictionaryValidateGroup : IDictionaryValidate, INotifyPropertyChanged
	{
		private readonly string[] _groups;
        private readonly IDictionaryAdapter _adapter;
		private readonly string[] _propertyNames;

		public DictionaryValidateGroup(string[] groups, IDictionaryAdapter adapter)
		{
			_groups = groups;
			_adapter = adapter;

			_propertyNames = (from property in _adapter.Meta.Properties.Values
					  from groupings in property.Behaviors.OfType<GroupAttribute>()
					  where Array.IndexOf(_groups, groupings.Group) >= 0
					  select property.PropertyName).Distinct().ToArray();

			if (_propertyNames.Length > 0 && adapter.CanNotify)
			{
				_adapter.PropertyChanged += (sender, args) =>
				{
					if (PropertyChanged != null)
					{
						PropertyChanged(this, args);
					}
				};
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;
        
		public bool CanValidate
		{
			get { return _adapter.CanValidate; }
			set { _adapter.CanValidate = value; }
		}

		public bool IsValid
		{
			get { return string.IsNullOrEmpty(Error); }
		}

		public string Error
		{
			get
			{
				return string.Join(Environment.NewLine,
					_propertyNames.Select(propertyName => _adapter[propertyName])
					.Where(errors => !string.IsNullOrEmpty(errors)).ToArray());
			}
		}

		public string this[string columnName]
		{
			get
			{
				if (Array.IndexOf(_propertyNames, columnName) >= 0)
				{
					return _adapter[columnName];
				}
				return string.Empty;
			}
		}

		public IDictionaryValidate ValidateGroups(params string[] groups)
		{
			groups = _groups.Union(groups).ToArray();
			return new DictionaryValidateGroup(groups, _adapter);
		}
        
		public IEnumerable<IDictionaryValidator> Validators
		{
			get { return _adapter.Validators; }
		}

		public void AddValidator(IDictionaryValidator validator)
		{
			throw new NotSupportedException();
		}
	}
}
