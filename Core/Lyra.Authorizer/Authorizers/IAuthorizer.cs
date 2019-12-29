﻿using Lyra.Core.Blocks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Lyra.Authorizer.Authorizers
{
    public interface IAuthorizer
    {
        Task<(APIResultCodes, AuthorizationSignature)> Authorize<T>(T tblock);
        Task<APIResultCodes> Commit<T>(T tblock);
    }
}