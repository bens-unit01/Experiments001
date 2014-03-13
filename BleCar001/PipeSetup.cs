/* Copyright (c) 2013 Nordic Semiconductor. All Rights Reserved.
 *
 * The information contained herein is property of Nordic Semiconductor ASA.
 * Terms and conditions of usage are described in detail in NORDIC
 * SEMICONDUCTOR STANDARD SOFTWARE LICENSE AGREEMENT. 
 *
 * Licensees are granted free, non-transferable use of the information. NO
 * WARRANTY of ANY KIND is provided. This heading must NOT be removed from
 * the file.
 *
 */

using System;
using Nordicsemi;

namespace nRFUart
{
    internal class PipeSetup
    {
        /* Public properties for accessing discovered pipe IDs */
        public int UartRxPipe { get; private set; }
        public int UartTxPipe { get; private set; }

        MasterEmulator masterEmulator;

        public PipeSetup(MasterEmulator master)
        {
            masterEmulator = master;
        }

        /// <summary>
        /// Pipe setup is performed by sequentially adding services, characteristics and
        /// descriptors. Pipes can be added to the characteristics and descriptors one wants
        /// to have access to from the application during runtime. A pipe assignment must
        /// be stated directly after the characteristic or descriptor it shall apply for.
        /// The pipe type chosen will affect what operations can be performed on the pipe
        /// at runtime. <see cref="Nordicsemi.PipeType"/>.
        /// </summary>
        /// 
        public void PerformPipeSetup()
        {
            /* GAP service */
            BtUuid uartOverBtleUuid = new BtUuid("6e400001b5a3f393e0a9e50e24dcca9e");
            masterEmulator.SetupAddService(uartOverBtleUuid, PipeStore.Remote);

            /* UART RX characteristic (RX from peripheral's viewpoint) */
            BtUuid uartRxUuid = new BtUuid("6e400002b5a3f393e0a9e50e24dcca9e");
            int uartRxMaxLength = 20;
            byte[] uartRxData = null;
            masterEmulator.SetupAddCharacteristicDefinition(uartRxUuid, uartRxMaxLength,
                uartRxData);
            /* Using pipe type Transmit to enable write operations */
            UartRxPipe = masterEmulator.SetupAssignPipe(PipeType.Transmit);

            /* UART TX characteristic (TX from peripheral's viewpoint) */
            BtUuid UartTxUuid = new BtUuid("6e400003b5a3f393e0a9e50e24dcca9e");
            int uartTxMaxLength = 20;
            byte[] uartTxData = null;
            masterEmulator.SetupAddCharacteristicDefinition(UartTxUuid, uartTxMaxLength,
                uartTxData);
            /* Using pipe type Receive to enable notify operations */
            UartTxPipe = masterEmulator.SetupAssignPipe(PipeType.Receive);
        }
    }
}
