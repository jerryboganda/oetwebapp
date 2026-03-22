import React from "react";
import {
  IconCircleCheck,
  IconClipboardCheck,
  IconClock,
  IconMessageCircle,
} from "@tabler/icons-react";
import { Card, CardBody, Badge } from "reactstrap";
import { IconProps } from "@tabler/icons-react";

interface TimelineItem {
  title: string;
  time: string;
  description: string;
  moreInfoLink: string;
  color:
    | "primary"
    | "secondary"
    | "success"
    | "info"
    | "warning"
    | "danger"
    | "dark";
  icon: React.ComponentType<IconProps>;
  tags?: string[];
}

const timelineData: TimelineItem[] = [
  {
    title: "Task Finished",
    time: "10 Min ago",
    description:
      "The quick, brown fox jumps over a lazy dog. DJs flock by when MTV ax quiz prog.",
    moreInfoLink: "#",
    color: "primary",
    icon: IconCircleCheck,
  },
  {
    title: "Task Overdue",
    time: "50 Min ago",
    description:
      "Bawds jog, flick quartz, vex nymphs. Waltz, bad nymph, for quick jigs vex!",
    moreInfoLink: "#",
    color: "secondary",
    icon: IconClock,
    tags: ["Design", "HTML"],
  },
  {
    title: "New Task",
    time: "10 hours ago",
    description:
      "Brick quiz whangs jumpy veldt fox. Bright vixens jump; dozy fowl quack.",
    moreInfoLink: "#",
    color: "success",
    icon: IconClipboardCheck,
  },
  {
    title: "New Comment",
    time: "Yesterday",
    description:
      "Quick zephyrs blow, vexing daft Jim. Sex-charged fop blew my junk TV quiz.",
    moreInfoLink: "#",
    color: "info",
    icon: IconMessageCircle,
  },
  {
    title: "New Task",
    time: "10 hours ago",
    description:
      "Brick quiz whangs jumpy veldt fox. Bright vixens jump; dozy fowl quack.",
    moreInfoLink: "#",
    color: "warning",
    icon: IconMessageCircle,
  },
];

const TimeLine1: React.FC = () => {
  return (
    <Card className="h-100">
      <CardBody>
        <ul className="app-timeline-box list-unstyled">
          {timelineData.map((item, index) => {
            const Icon = item.icon;
            return (
              <li className="timeline-section" key={index}>
                <div className="timeline-icon me-2">
                  <span
                    className={`text-light-${item.color} w-35 h-35 d-flex align-items-center justify-content-center rounded-circle`}
                  >
                    <Icon size={18} />
                  </span>
                </div>
                <div
                  className={`timeline-content bg-light-${item.color} border-0 p-3 rounded`}
                >
                  <div className="d-flex justify-content-between align-items-center">
                    <h6 className={`text-${item.color}-dark mb-1`}>
                      {item.title}
                    </h6>
                    <small className="text-muted">{item.time}</small>
                  </div>
                  <p className="text-dark mb-1">
                    {item.description}{" "}
                    <a href={item.moreInfoLink} className="text-primary">
                      [more info]
                    </a>
                  </p>
                  {item.tags && (
                    <div className="mt-2">
                      {item.tags.map((tag, tagIndex) => (
                        <Badge key={tagIndex} color="primary" className="me-2">
                          {tag}
                        </Badge>
                      ))}
                    </div>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      </CardBody>
    </Card>
  );
};

export default TimeLine1;
