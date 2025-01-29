// TrafficControl



type Events =
  | Clock of int
  | NsButton
  | EwButton


type TrafficLight = string

type Msg<'Event> =
  | Append of 'Event list
  | 
