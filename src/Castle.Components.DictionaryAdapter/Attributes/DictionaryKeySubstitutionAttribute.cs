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

	/// <summary>
	/// Substitutes part of key with another string.
	/// </summary>
	[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
	public class DictionaryKeySubstitutionAttribute : DictionaryBehaviorAttribute, IDictionaryKeyBuilder
	{
		private readonly String oldValue;
		private readonly String newValue;

		/// <summary>
		/// Initializes a new instance of the <see cref="DictionaryKeySubstitutionAttribute"/> class.
		/// </summary>
		/// <param name="oldValue">The old value.</param>
		/// <param name="newValue">The new value.</param>
		public DictionaryKeySubstitutionAttribute(String oldValue, String newValue)
		{
			this.oldValue = oldValue;
			this.newValue = newValue;
		}

		String IDictionaryKeyBuilder.GetKey(IDictionaryAdapter dictionaryAdapter, String key, PropertyDescriptor property)
		{
			return key.Replace(oldValue, newValue);
		}
	}
}