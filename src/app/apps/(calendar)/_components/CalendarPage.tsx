"use client";
import React, { ReactNode, useRef, useState } from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Col,
  FormGroup,
  Input,
  Label,
  Row,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Slider from "react-slick";
import {
  IconBriefcase,
  IconCake,
  IconCalendarEvent,
  IconCircle,
  IconGlassFullFilled,
  IconPhoto,
  IconPlane,
  IconStack2,
} from "@tabler/icons-react";
import CalendarCard from "@/app/apps/(calendar)/_components/CalendarCard";
// import CalendarCard from "./_components/CalendarCard";

const eventData = [
  {
    title: "International Women's Day",
    description:
      "Celebrated to recognize the social and political achievements of women.",
    date: "08 Mar 2024",
  },
  {
    title: "World Book Day",
    description: "Celebrated to promote reading, publishing, and copyright.",
    date: "23 Apr 2024",
  },
  {
    title: "World Refugee Day",
    description: "Observed to honor the courage and resilience of refugees.",
    date: "20 Jun 2024",
  },
  {
    title: "World Humanitarian Day",
    description:
      "A day to recognize humanitarian personnel and those who lost their lives.",
    date: "19 Aug 2024",
  },
  {
    title: "International Day of Peace",
    description: "Promotes peace and cessation of war across the globe.",
    date: "21 Sep 2024",
  },
];

type DraggableEvent = {
  id: number;
  title: string;
  icon: ReactNode;
  className: string;
};

const Calendar = () => {
  const [draggableEvents, setDraggableEvents] = useState<DraggableEvent[]>([
    {
      id: 1,
      title: "Meeting Time",
      icon: <IconBriefcase size={16} className="me-2" />,
      className: "event-primary",
    },
    {
      id: 2,
      title: "Holiday",
      icon: <IconPhoto size={16} className="me-2" />,
      className: "event-success",
    },
    {
      id: 3,
      title: "Tour Planning",
      icon: <IconPlane size={16} className="me-2" />,
      className: "event-warning",
    },
    {
      id: 4,
      title: "Birthday",
      icon: <IconCake size={16} className="me-2" />,
      className: "event-info",
    },
    {
      id: 5,
      title: "Lunch Break",
      icon: <IconGlassFullFilled size={16} className="me-2" />,
      className: "event-secondary",
    },
  ]);
  const externalRef = useRef<HTMLDivElement | null>(null);
  const dropRemoveRef = useRef<HTMLInputElement | null>(null);

  const settings = {
    dots: false,
    speed: 1000,
    slidesToShow: 3,
    centerMode: true,
    arrows: false,
    vertical: true,
    verticalSwiping: true,
    focusOnSelect: true,
    autoplay: true,
    autoplaySpeed: 4000,
  };

  return (
    <div className="container-fluid">
      <Breadcrumbs
        mainTitle="Calendar"
        title="Apps"
        path={["Calendar"]}
        Icon={IconStack2}
      />
      <Row className="m-1 calendar app-fullcalendar">
        <Col xxl="3">
          <Row>
            <Col md="6" xxl="12">
              <Card>
                <CardHeader>
                  <h5>Draggable Events</h5>
                </CardHeader>
                <CardBody>
                  <div id="external-events" ref={externalRef}>
                    {draggableEvents.map((ev) => (
                      <div
                        key={ev.id}
                        className={`list-event fc-event ${ev.className}`}
                        data-classname={ev.className}
                      >
                        {ev.icon}
                        {ev.title}
                      </div>
                    ))}

                    <FormGroup check className="calendar-remove-check mt-3">
                      <Input
                        type="checkbox"
                        id="drop-remove"
                        innerRef={dropRemoveRef}
                      />
                      <Label htmlFor="drop-remove" check>
                        Remove After Drop
                      </Label>
                    </FormGroup>
                  </div>
                </CardBody>
              </Card>
            </Col>

            <Col md="6" xxl="12">
              <Card>
                <CardHeader>
                  <h5>Events Update List</h5>
                </CardHeader>
                <CardBody>
                  <Slider className="event-container slider" {...settings}>
                    {eventData.map((event, idx) => (
                      <div className="event-box" key={idx}>
                        <h6 className="mb-3 d-flex align-items-center">
                          <IconCircle size={14} className="me-2" />
                          {event.title}
                        </h6>
                        <p className="mb-2 text-secondary f-s-13">
                          {event.description}
                        </p>
                        <p className="f-s-13 text-end mb-0 d-flex justify-content-end align-items-center">
                          <IconCalendarEvent size={14} className="me-1" />
                          {event.date}
                        </p>
                      </div>
                    ))}
                  </Slider>
                </CardBody>
              </Card>
            </Col>
          </Row>
        </Col>

        <Col xxl="9" className="mt-3 mt-md-0">
          <CalendarCard
            externalRef={externalRef}
            dropRemoveRef={dropRemoveRef}
            onExternalRemove={(el) => {
              const title = el.textContent?.trim();
              setDraggableEvents((prev) =>
                prev.filter((event: DraggableEvent) => event.title !== title)
              );
            }}
          />
        </Col>
      </Row>
    </div>
  );
};

export default Calendar;
