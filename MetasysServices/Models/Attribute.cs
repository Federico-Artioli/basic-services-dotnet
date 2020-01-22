﻿using System;
using System.Collections.Generic;
using System.Text;

namespace JohnsonControls.Metasys.BasicServices
{
    /// <summary>
    /// DTO for the attribute of an Object
    /// </summary>
    public class Attribute
    {
        public int Id { get; set; }
        public string Description { get; set; }
        /// Returns a value indicating whither this instance has values equal to a specified object.
        /// </summary>
        /// <param name="obj"></param>
        public override bool Equals(object obj)
        {
            if (obj != null && obj is Attribute)
            {
                var o = (Attribute)obj;
                // Compare each properties one by one for better performance
                return this.Id == o.Id && this.Description == o.Description
                ;
            }
            return false;
        }

        /// <summary></summary>
        public override int GetHashCode()
        {
            var code = 13;
            // Calculate hash on each properties one by one
            code = (code * 7) + this.Id.GetHashCode();
            if (Id != null)
                code = (code * 7) + Id.GetHashCode();
            if (this.Description != null)
                code = (code * 7) + Description.GetHashCode();           
            return code;
        }
    }
}