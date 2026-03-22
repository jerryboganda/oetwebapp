import React from "react";
import { IconCircleFilled } from "@tabler/icons-react";
import { Card, CardBody, Badge } from "reactstrap";

interface TimelineItem {
  title: string;
  time: string;
  description: string;
  moreInfoLink: string;
  color: "primary" | "secondary" | "success" | "info" | "warning";
  tags?: string[];
}

const timelineData: TimelineItem[] = [
  {
    title: "Task Finished",
    time: "10 min ago",
    description:
      "The quick, brown fox jumps over a lazy dog. DJs flock by when MTV ax quiz prog.",
    moreInfoLink: "#",
    color: "primary",
  },
  {
    title: "Task Overdue",
    time: "50 min ago",
    description:
      "Bawds jog, flick quartz, vex nymphs. Waltz, bad nymph, for quick jigs vex!",
    moreInfoLink: "#",
    color: "secondary",
    tags: ["Design", "HTML"],
  },
  {
    title: "New Task",
    time: "10 hours ago",
    description:
      "Brick quiz whangs jumpy veldt fox. Bright vixens jump; dozy fowl quack.",
    moreInfoLink: "#",
    color: "success",
  },
  {
    title: "New Task",
    time: "10 hours ago",
    description:
      "Quick wafting zephyrs vex bold Jim. Sex-charged fop blew my junk TV quiz.",
    moreInfoLink: "#",
    color: "info",
  },
];

const TimeLine2: React.FC = () => {
  return (
    <Card className="h-100">
      <CardBody>
        <ul className="app-side-timeline list-unstyled">
          {timelineData.map((item, index) => (
            <li
              key={index}
              className={`side-timeline-section ${index % 2 === 0 ? "left-side" : "right-side"}`}
            >
              <div className="side-timeline-icon">
                <span
                  className={`text-light-${item.color} h-25 w-25 d-flex align-items-center justify-content-center rounded-circle`}
                >
                  <IconCircleFilled
                    size={16}
                    className="rounded-circle animate__animated animate__zoomIn animate__infinite animate__slower"
                  />
                </span>
              </div>
              <div className="timeline-content">
                <div className="mb-2">
                  <h6 className={`text-${item.color} mb-1`}>{item.title}</h6>
                  <small className="text-muted">{item.time}</small>
                </div>
                <p className="mb-1 text-dark">
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
          ))}
        </ul>
      </CardBody>
    </Card>
  );
};

export default TimeLine2;
