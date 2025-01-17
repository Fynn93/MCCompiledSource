﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mc_compiled.NBT
{
    /// <summary>
    /// A local 2-axis rotation.
    /// </summary>
    public struct RotationNBT
    {
        public float x, z;

        public RotationNBT(float x, float z)
        {
            this.x = x;
            this.z = z;
        }

        public NBTList ToNBT(string name)
        {
            return new NBTList()
            {
                name = name,
                listType = TAG.Float,
                values = new NBTFloat[]
                {
                    new NBTFloat() { name = "", value = x },
                    new NBTFloat() { name = "", value = z }
                }
            };
        }
    }
}
