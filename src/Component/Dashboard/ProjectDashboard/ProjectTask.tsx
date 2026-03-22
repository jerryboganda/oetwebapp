import React, { useState } from "react";
import Slider, { Settings } from "react-slick";
import { Copy, Group, MoreHoriz } from "iconoir-react";
import { Col, Tooltip } from "reactstrap";

const ProjectTask = () => {
  const [tooltipOpen, setTooltipOpen] = useState<{ [key: string]: boolean }>(
    {}
  );

  const toggleTooltip = (id: string) => {
    setTooltipOpen((prev) => ({
      ...prev,
      [id]: !prev[id],
    }));
  };
  const tasks = [
    {
      id: 1,
      type: "task",
      bgClass: "bg-danger-300",
      title: "Finalize Project Proposal",
      titleClass: "text-danger-dark",
      hasAvatars: true,
      avatars: [
        {
          src: "/images/avatar/4.png",
          bgClass: "bg-primary",
          tooltip: "",
        },
        {
          src: "/images/avatar/5.png",
          bgClass: "bg-success",
          tooltip: "Lennon Briggs",
        },
        {
          src: "/images/avatar/6.png",
          bgClass: "bg-danger",
          tooltip: "Maya Horton",
        },
      ],
      progress: 68,
      progressClass: "bg-danger-dark",
    },
    {
      id: 2,
      type: "meeting",
      bgClass: "bg-primary-300",
      title: "Meeting",
      titleClass: "text-primary-dark",
      hasAvatars: false,
      icons: [
        {
          iconClass: <MoreHoriz height={16} width={16} className="f-s-18" />,
          wrapperClass: "bg-white-300 text-info-dark",
        },
        {
          iconClass: <Copy height={16} width={16} className="f-s-18" />,
          wrapperClass: "bg-white-300 text-info-dark",
        },
      ],
    },
    {
      id: 3,
      type: "task",
      bgClass: "bg-warning-300",
      title: "Design Homepage Layout",
      titleClass: "text-warning-dark",
      hasAvatars: true,
      avatars: [
        {
          src: "/images/avatar/3.png",
          bgClass: "bg-primary",
          tooltip: "",
        },
        {
          src: "/images/avatar/7.png",
          bgClass: "bg-info",
          tooltip: "Sophia Turner",
        },
        {
          src: "/images/avatar/8.png",
          bgClass: "bg-warning",
          tooltip: "Lucas Green",
        },
      ],
      progress: 35,
      progressClass: "bg-warning-dark",
    },
    {
      id: 4,
      type: "meeting",
      bgClass: "bg-info-300",
      title: "Meeting",
      titleClass: "text-info-dark",
      hasAvatars: false,
      icons: [
        {
          iconClass: <MoreHoriz height={16} width={16} className="f-s-18" />,
          wrapperClass: "bg-white-300 text-info-dark",
        },
        {
          iconClass: <Copy height={16} width={16} className="f-s-18" />,
          wrapperClass: "bg-white-300 text-info-dark",
        },
      ],
    },
    {
      id: 5,
      type: "meeting",
      bgClass: "bg-success-300",
      title: "Meeting",
      titleClass: "text-success-dark",
      hasAvatars: false,
      icons: [
        {
          iconClass: <MoreHoriz height={16} width={16} className="f-s-18" />,
          wrapperClass: "bg-white-300 text-info-dark",
        },
        {
          iconClass: <Copy height={16} width={16} className="f-s-18" />,
          wrapperClass: "bg-white-300 text-info-dark",
        },
      ],
    },
    {
      id: 6,
      type: "task",
      bgClass: "bg-info-300",
      title: "Develop API Integration",
      titleClass: "text-info-dark",
      hasAvatars: true,
      avatars: [
        {
          src: "/images/avatar/4.png",
          bgClass: "bg-info",
          tooltip: "",
        },
        {
          src: "/images/avatar/6.png",
          bgClass: "bg-info",
          tooltip: "Michael Johnson",
        },
        {
          src: "/images/avatar/5.png",
          bgClass: "bg-warning",
          tooltip: "Emily Brown",
        },
      ],
      progress: 60,
      progressClass: "bg-info-dark",
    },
    {
      id: 7,
      type: "task",
      bgClass: "bg-success-300",
      title: "Test User Feedback",
      titleClass: "text-success-dark",
      hasAvatars: true,
      avatars: [
        {
          src: "/images/avatar/9.png",
          bgClass: "bg-primary",
          tooltip: "",
        },
        {
          src: "/images/avatar/10.png",
          bgClass: "bg-info",
          tooltip: "Alice Smith",
        },
        {
          src: "/images/avatar/11.png",
          bgClass: "bg-success",
          tooltip: "John Doe",
        },
      ],
      progress: 80,
      progressClass: "bg-success-dark",
    },
    // Add more cards as needed
  ];
  const sliderSettings: Settings = {
    dots: false,
    speed: 1000,
    slidesToShow: 3,
    arrows: false,
    vertical: true,
    verticalSwiping: true,
    focusOnSelect: true,
    autoplay: true,
    autoplaySpeed: 1000,
  };
  return (
    <Col md="6" lg="5" xxl="3">
      <div className="p-3">
        <h5>Today Tasks</h5>
      </div>
      <div className="card">
        <div className="card-body">
          <div className="task-container slider">
            <Slider className="task-container" {...sliderSettings}>
              {tasks.map((task) => (
                <div key={task.id} className={`card task-card ${task.bgClass}`}>
                  {task.hasAvatars ? (
                    <div className="card-body">
                      <h6 className={`${task.titleClass} txt-ellipsis-1`}>
                        {task.title}
                      </h6>
                      <ul className="avatar-group justify-content-start my-3">
                        {task?.avatars?.map((avatar, index) => (
                          <li
                            key={index}
                            id={`tooltip-${task.id}-${index}`}
                            className={`h-35 w-35 d-flex-center b-r-50 overflow-hidden ${avatar.bgClass}`}
                          >
                            <img
                              alt="avatar"
                              className="img-fluid"
                              src={avatar.src}
                            />
                            {avatar.tooltip && (
                              <Tooltip
                                target={`tooltip-${task.id}-${index}`}
                                isOpen={
                                  tooltipOpen[`tooltip-${task.id}-${index}`] ||
                                  false
                                }
                                toggle={() =>
                                  toggleTooltip(`tooltip-${task.id}-${index}`)
                                }
                              >
                                {avatar.tooltip}
                              </Tooltip>
                            )}
                          </li>
                        ))}
                      </ul>
                      <div className="d-flex justify-content-between align-items-center">
                        <div
                          className="progress w-100"
                          role="progressbar"
                          aria-valuenow={task.progress}
                          aria-valuemin={0}
                          aria-valuemax={100}
                        >
                          <div
                            className={`progress-bar ${task.progressClass} progress-bar-striped progress-bar-animated`}
                            style={{ width: `${task.progress}%` }}
                          ></div>
                        </div>
                        <span className="badge bg-white-300 text-danger-dark ms-2">
                          + {task.progress}%
                        </span>
                      </div>
                    </div>
                  ) : (
                    <div className="d-flex justify-content-between align-items-center rounded p-1">
                      <span
                        className={`h-35 w-35 d-flex-center rounded ${task.bgClass}`}
                      >
                        <Group height={16} width={16} className="f-s-18" />
                      </span>
                      <h6 className="mb-0 txt-ellipsis-1">{task.title}</h6>
                      <div className="d-flex gap-2">
                        {task?.icons?.map((icon, index) => (
                          <span
                            key={index}
                            className={`w-35 h-35 rounded p-2 d-flex-center ${icon.wrapperClass}`}
                          >
                            {icon.iconClass}
                          </span>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              ))}
            </Slider>
          </div>
        </div>
      </div>
    </Col>
  );
};

export default ProjectTask;
