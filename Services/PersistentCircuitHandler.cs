using Microsoft.AspNetCore.Components.Server.Circuits;

namespace YKCoatings.Services
{
    public class PersistentCircuitHandler : CircuitHandler
    {
        private readonly IServiceProvider _serviceProvider;

        public PersistentCircuitHandler(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            await base.OnCircuitOpenedAsync(circuit, cancellationToken);
        }

        public override async Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            await base.OnCircuitClosedAsync(circuit, cancellationToken);
        }
    }
}