

#include <stdbool.h>
#include <stdint.h>
#include "nrf_delay.h"
#include "nrf_gpio.h"
#include "nrf_gpiote.h"
#include "boards.h"


#define TIMER_PRESCALER       (2)       /**< Prescaler setting for timers. */
#define LED_INTENSITY_HIGH    (126U)    /**< High intensity. */
#define LED_INTENSITY_LOW     (10U)     /**< Low intensity. */
#define LED_OFF               (1U)      /**< Led off. */
#define LED_INTENSITY_HALF    (63U)    /**< Half intensity. Used to calculate timer parameters. */



 void pwm_init(uint32_t pwm_output_pin_number);
 void pwm_set(uint8_t new_value);

