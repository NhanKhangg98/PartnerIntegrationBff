using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PartnerIntegration.Core.Interfaces;

public interface IPartnerVerificationClient
{
    Task<bool> VerifyPartnerAsync(string partnerId, CancellationToken ct = default);
}
