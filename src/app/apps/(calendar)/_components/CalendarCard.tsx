import React, { useEffect, useRef, useState } from "react";
import { Card, CardBody } from "reactstrap";
import dayGridPlugin from "@fullcalendar/daygrid";
import interactionPlugin, {
  Draggable,
  EventReceiveArg,
} from "@fullcalendar/interaction";
import listPlugin from "@fullcalendar/list";
import FullCalendar from "@fullcalendar/react";
import timeGridPlugin from "@fullcalendar/timegrid";

interface Props {
  externalRef: React.RefObject<HTMLDivElement | null>;
  dropRemoveRef: React.RefObject<HTMLInputElement | null>;
  onExternalRemove?: (el: HTMLElement) => void;
}

interface CalendarEvent {
  id?: string;
  title?: string;
  start: string;
  end?: string;
  groupId?: string;
  display?: string;
  allDay?: boolean;
  extendedProps?: {
    iconHtml?: string;
  };
}

const FullCalendarCard: React.FC<Props> = ({
  externalRef,
  dropRemoveRef,
  onExternalRemove,
}) => {
  const calendarRef = useRef<FullCalendar | null>(null);
  const draggableRef = useRef<Draggable | null>(null);
  const [events, setEvents] = useState<CalendarEvent[]>([
    { title: "Events", start: "2025-07-01", end: "2025-07-02" },
    { title: "Events", start: "2025-07-08", end: "2025-07-12" },
    { title: "Meeting", start: "2025-07-12T10:30:00" },
    { title: "Lunch", start: "2025-07-12T12:00:00" },
    { title: "Meeting", start: "2025-07-12T14:30:00" },
    { title: "Happy Hour", start: "2025-07-12T17:20:00" },
    { title: "Dinner", start: "2025-07-12T20:10:00" },
    {
      groupId: "availableForMeeting",
      start: "2025-07-11T10:00:00",
      end: "2025-07-11T16:00:00",
      display: "background",
    },
  ]);

  useEffect(() => {
    if (externalRef.current && !draggableRef.current) {
      draggableRef.current = new Draggable(externalRef.current, {
        itemSelector: ".fc-event",
        eventData: (el: any) => ({
          title: el.textContent?.trim() || "",
          className: el.getAttribute("data-classname") || "",
          extendedProps: { iconHtml: el.innerHTML },
        }),
      });
    }

    return () => {
      draggableRef.current?.destroy();
      draggableRef.current = null;
    };
  }, [externalRef]);

  const handleEventReceive = (info: EventReceiveArg) => {
    const newEvent: CalendarEvent = {
      id: `external-${Date.now()}`,
      title: info.event.title,
      start: info.event.startStr,
      allDay: info.event.allDay,
      extendedProps: info.event.extendedProps,
    };

    if (info.event.endStr) {
      newEvent.end = info.event.endStr;
    }

    setEvents((prevEvents) => [...prevEvents, newEvent]);

    if (dropRemoveRef.current?.checked && onExternalRemove) {
      onExternalRemove(info.draggedEl);
    }
  };

  const handleCustomEventAdd = () => {
    const dateStr = prompt("Enter a date in YYYY-MM-DD format");
    if (!dateStr) return;

    const date = new Date(dateStr + "T00:00:00");

    if (!isNaN(date.valueOf())) {
      const formattedDate = dateStr;
      const newEvent: CalendarEvent = {
        id: `dynamic-${Date.now()}`,
        title: "Dynamic Event",
        start: formattedDate,
        allDay: true,
      };

      setEvents((prevEvents) => [...prevEvents, newEvent]);
      alert("Event added.");
    } else {
      alert("Invalid date.");
    }
  };

  const handleEventRemove = (eventId: string) => {
    setEvents((prevEvents) =>
      prevEvents.filter((event) => event.id !== eventId)
    );
  };

  const renderEventWithDeleteButton = (arg: any) => {
    const { event } = arg;
    const iconHtml = event.extendedProps?.iconHtml || event.title;

    return (
      <div className="fc-custom-event d-flex align-items-center justify-content-between">
        <div className="fc-event-content">
          {iconHtml ? (
            <span dangerouslySetInnerHTML={{ __html: iconHtml }} />
          ) : (
            <span>{event.title}</span>
          )}
        </div>
        <button
          className="btn icon-btn p-0 w-20 h-20 fc-delete-btn cursor-pointer"
          onClick={(e) => {
            e.stopPropagation();
            if (event.id) {
              handleEventRemove(event.id);
            } else {
              setEvents((prevEvents) =>
                prevEvents.filter(
                  (ev) =>
                    !(ev.title === event.title && ev.start === event.startStr)
                )
              );
            }
          }}
          aria-label={`Delete event ${event.title}`}
        >
          ✕
        </button>
      </div>
    );
  };

  return (
    <Card>
      <CardBody className="app-calendar">
        <FullCalendar
          ref={calendarRef}
          plugins={[
            dayGridPlugin,
            timeGridPlugin,
            interactionPlugin,
            listPlugin,
          ]}
          initialView="dayGridMonth"
          headerToolbar={{
            left: "prev,next,addEventButton",
            center: "title",
            right: "dayGridMonth,timeGridWeek,timeGridDay,listWeek",
          }}
          customButtons={{
            addEventButton: {
              text: "Add event...",
              click: handleCustomEventAdd,
            },
          }}
          editable
          droppable
          selectable
          selectMirror
          dayMaxEvents
          eventReceive={handleEventReceive}
          eventContent={renderEventWithDeleteButton}
          events={events}
          eventTimeFormat={{
            hour: "2-digit",
            minute: "2-digit",
            hour12: false,
          }}
          height="auto"
        />
      </CardBody>
    </Card>
  );
};

export default FullCalendarCard;
