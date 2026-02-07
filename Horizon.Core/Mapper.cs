using AutoMapper;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core
{
    public static class MapperInstance
    {
        private static IMapper _mapper;
        public static IMapper Current
        {
            get { return _mapper; }
            set { _mapper = value; }
        }
    }
}
