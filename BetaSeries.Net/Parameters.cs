﻿using System.Collections.Generic;
using System.Dynamic;

namespace BetaSeries.Net
{
    public class Parameters : DynamicObject
    {
        #region Private Fields

        // The inner dictionary.
        private Dictionary<string, object> _dictionary = new Dictionary<string, object>();

        #endregion Private Fields

        #region Public Methods

        public override string ToString()
        {
            string output = "?";

            foreach (var entry in _dictionary)
            {
                output += $"{entry.Key}={entry.Value}&";
            }

            return output;
        }

        // If you try to get a value of a property
        // not defined in the class, this method is called.
        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            // Converting the property name to lowercase
            // so that property names become case-insensitive.
            string name = binder.Name.ToLower();

            // If the property name is found in a dictionary,
            // set the result parameter to the property value and return true.
            // Otherwise, return false.
            return _dictionary.TryGetValue(name, out result);
        }

        // If you try to set a value of a property that is
        // not defined in the class, this method is called.
        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            // Converting the property name to lowercase
            // so that property names become case-insensitive.
            _dictionary[binder.Name.ToLower()] = value;

            // You can always add a value to a dictionary,
            // so this method always returns true.
            return true;
        }

        #endregion Public Methods
    }
}