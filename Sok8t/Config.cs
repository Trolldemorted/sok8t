using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sok8t;

internal record Config(
    int LocalPort,
    int DestinationPort,
    string DestinationImage,
    string Namespace,
    CancellationToken CancelToken);
