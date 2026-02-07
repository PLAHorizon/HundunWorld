using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract
{
    //
    // 摘要:
    //     Defines the set of data required to apply authorization rules to a resource.
    public interface IAuthorizeData
    {
        /// <summary>
        /// Gets or sets the policy name that determines access to the resource.
        /// </summary>
        string? Policy { get; set; }
        //
        // 摘要:
        //     Gets or sets a comma delimited list of roles that are allowed to access the resource.
        string? Roles { get; set; }
        //
        // 摘要:
        //     Gets or sets a comma delimited list of schemes from which user information is
        //     constructed.
        string? AuthenticationSchemes { get; set; }
    }
}
