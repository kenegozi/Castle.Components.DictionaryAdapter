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

	public abstract partial class DictionaryAdapterBase
	{
		private int suppressEditingCount = 0;
		private Stack<Dictionary<string, object>> updates;
		private HashSet<IEditableObject> editDependencies;

		public bool CanEdit
		{
			get { return suppressEditingCount == 0 && updates != null; }
			set { updates = value ? new Stack<Dictionary<string, object>>() : null; }
		}

		public bool IsEditing
		{
			get { return updates != null && updates.Count > 0; }
		}

		public bool SupportsMultiLevelEdit { get; set; }

		public bool IsChanged
		{
			get
			{
				if (IsEditing && updates.Any(level => level.Count > 0))
					return true;

				return Properties.Values
					.Where(prop => typeof(IChangeTracking).IsAssignableFrom(prop.PropertyType))
					.Select(prop => GetProperty(prop.PropertyName))
					.Cast<IChangeTracking>().Any(track => track.IsChanged);
			}
		}

		public void BeginEdit()
		{
			if (CanEdit && (!IsEditing || SupportsMultiLevelEdit))
			{
				updates.Push(new Dictionary<string, object>());
			}
		}

		public void CancelEdit()
		{
			if (IsEditing)
			{
				if (editDependencies != null)
				{
					foreach (var editDependency in editDependencies.ToArray())
					{
						editDependency.CancelEdit();
					}
					editDependencies.Clear();
				}

				updates.Pop();

				Invalidate();
			}
		}

		public void EndEdit()
		{
			if (IsEditing)
			{
				using (SuppressEditingBlock())
                {
					var top = updates.Pop();

					if (top.Count > 0)
					{
						using (TrackReadonlyPropertyChanges())
						{
							foreach (var update in top.ToArray())
							{
								object value = update.Value;
								SetProperty(update.Key, ref value);
							}
						}
					}

					if (editDependencies != null)
					{
						foreach (var editDependency in editDependencies.ToArray())
						{
							editDependency.EndEdit();
						}
						editDependencies.Clear();
					}
				}
			}
		}

		public void RejectChanges()
		{
			CancelEdit();
		}

		public void AcceptChanges()
		{
			EndEdit();
		}

		public IDisposable SuppressEditingBlock()
		{
			return new SuppressEditingScope(this);
		}

		public void SuppressEditing()
		{
			++suppressEditingCount;
		}

		public void ResumeEditing()
		{
			--suppressEditingCount;
		}

		protected bool GetEditedProperty(string propertyName, out object propertyValue)
		{
			if (IsEditing) foreach (var level in updates.ToArray())
			{
				if (level.TryGetValue(propertyName, out propertyValue))
				{
					return true;
				}
			}
			propertyValue = null;
			return false;
		}

		protected bool EditProperty(string propertyName, object propertyValue)
		{
			if (IsEditing)
			{
				updates.Peek()[propertyName] = propertyValue;
				Invalidate();
				return true;
			}
			return false;
		}

		protected void AddEditDependency(IEditableObject editDependency)
		{
			if (IsEditing)
			{
				editDependencies = editDependencies ?? new HashSet<IEditableObject>();

				if (editDependencies.Add(editDependency))
				{
					editDependency.BeginEdit();
				}
			}
		}

		#region Nested Class: SuppressEditingScope

		class SuppressEditingScope : IDisposable
		{
			private readonly DictionaryAdapterBase adapter;

			public SuppressEditingScope(DictionaryAdapterBase adapter)
			{
				this.adapter = adapter;
				this.adapter.SuppressEditing();
			}

			public void Dispose()
			{
				this.adapter.ResumeEditing();
			}
		}

		#endregion
	}
}
