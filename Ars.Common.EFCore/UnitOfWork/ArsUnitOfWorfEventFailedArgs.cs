﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ars.Common.EFCore.UnitOfWork
{
    public class ArsUnitOfWorfEventFailedArgs : EventArgs
    {
        private Exception _exception;
        public ArsUnitOfWorfEventFailedArgs(Exception exception)
        {
            _exception = exception;
        }
    }
}
