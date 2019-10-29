﻿using JohnsonControls.Metasys.BasicServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace JohnsonControls.Metasys.ComServices
{
    [Guid("49E803EC-BED9-4a08-B42B-E0499864A169")]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class MetasysObjectsContainer : IMetasysObjectsContainer
    {
        public ComMetasysObject[] Objects { get; set; }

    }
}