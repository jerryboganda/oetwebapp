"use client";
import React, { useEffect, useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  Button,
  Input,
  CardFooter,
  Container,
  Row,
  Col,
  Table,
  Badge,
} from "reactstrap";

import Link from "next/link";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  IconCamera,
  IconMicrophone,
  IconMoodSmileBeam,
  IconPaperclip,
  IconSend,
  IconStack2,
} from "@tabler/icons-react";
import "glightbox/dist/css/glightbox.min.css";

let GLightbox: any = null;

if (typeof window !== "undefined") {
  GLightbox = require("glightbox");
  if (GLightbox.default) {
    GLightbox = GLightbox.default;
  }
}

const projectDetails = {
  name: "PolytronX",
  manager: "Leonor Hill",
  startDate: "10 Apr 2024",
  endDate: "20 Jul 2024",
  pricing: "$200k",
  status: "In progress",
};

const teamMembers = [
  {
    name: "Bette Hagenes",
    role: "Web Developer",
    imgSrc: "/images/avatar/7.png",
  },
  {
    name: "Fleta Walsh",
    role: "Web Designer",
    imgSrc: "/images/avatar/13.png",
  },
  { name: "Lenora", role: "UI/UX Designer", initial: "LR", color: "bg-dark" },
  {
    name: "Fleta Walsh",
    role: "React Developer",
    imgSrc: "/images/avatar/16.png",
  },
  {
    name: "Emery McKenzie",
    role: "Web Developer",
    initial: "EM",
    color: "bg-danger",
  },
  {
    name: "Bette Hagenes",
    role: "Web Designer",
    imgSrc: "/images/avatar/4.png",
  },
];

const projectActivities = [
  {
    id: 1,
    user: "Wilson",
    action: "added reaction in",
    badge: "#product website",
    time: "09.00AM",
    icon: "W",
  },
  {
    id: 2,
    user: "Image Upload",
    action: "2 image files and 2 videos uploaded",
    time: "12:45 PM",
    images: [
      "/images/draggable/02.jpg",
      "/images/draggable/04.jpg",
      "/images/draggable/01.jpg",
    ],
    icon: "W",
  },
  {
    id: 3,
    user: "Dane Wiza",
    action: "added reaction in",
    badge: "#product website",
    time: "09.00AM",
    feature: {
      title: "Need a feature",
      description: "Hello everyone, question on email marketing...",
      reactions: "10 Reactions",
      replies: "12 Replies",
    },
    icon: "W",
  },
  {
    id: 4,
    user: "Betty Mante",
    action: "Request joined",
    badge: "#reaserchteam",
    time: "4 days ago",
    buttons: true,
    icon: "W",
  },
  {
    id: 5,
    user: "Pinkie",
    action: "uploaded 2 attachments",
    badge: "#reaserchteam",
    time: "4 days ago",
    buttons: true,
    icon: "W",
  },
];

const projectDetails1 = {
  description:
    "An admin panel or a control panel is a system that enables administrators and other website workers to conduct various tasks like monitoring, maintaining, and controlling certain business processes. An admin dashboard is one of the core components of a control panel.",
  taskInfo:
    "The success of a project relies heavily on effective project management, which involves the careful planning, organizing, and controlling of resources to ensure that the project objectives are met. This includes defining the project scope, setting realistic timelines and budgets.",
  backgroundInfo:
    "A project is a planned endeavor that aims to achieve a specific goal within a defined timeframe. It involves a series of tasks and activities that are coordinated and executed by a team of individuals. Projects can vary in size, complexity, and scope, ranging from small-scale initiatives to large-scale undertakings that span across multiple departments or organizations.",
};

const projectFiles = [
  {
    name: "Admin Work",
    type: "file",
    count: "18 Files",
    size: "32GB",
    icon: "/images/icons/file.png",
  },
  {
    name: "React Data",
    type: "folder",
    count: "18 Files",
    size: "32GB",
    icon: "/images/icons/folder.png",
  },
  {
    name: "Project Details",
    type: "file",
    count: "18 Files",
    size: "32GB",
    icon: "/images/icons/file.png",
  },
  {
    name: "Project Reporting",
    type: "zip",
    count: "18 Files",
    size: "32GB",
    icon: "/images/icons/zip.png",
  },
];

const ProjectDetails = () => {
  const [mobileDropdownOpen, setMobileDropdownOpen] = useState(false);
  useEffect(() => {
    if (typeof window !== "undefined") {
      const lightbox = GLightbox({
        touchNavigation: true,
        loop: true,
        width: "90vw",
        height: "90vh",
      });

      return () => {
        lightbox.destroy();
      };
    }
    return;
  }, []);

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Project Details"
          title="Apps"
          path={["Project App", "Project Details"]}
          Icon={IconStack2}
        />
        <Row>
          <Col md="6" xxl="3" className="order-md-2 order-xxl-1">
            <Card>
              <CardHeader>
                <h5>Project Details</h5>
              </CardHeader>
              <CardBody>
                <Table className="project-details-table table-borderless align-middle mb-0">
                  <tbody>
                    <tr>
                      <td>
                        <strong>Name</strong>
                      </td>
                      <td className="text-end">{projectDetails.name}</td>
                    </tr>
                    <tr>
                      <td>
                        <strong>Manager</strong>
                      </td>
                      <td className="text-end">{projectDetails.manager}</td>
                    </tr>
                    <tr>
                      <td>
                        <strong>Start Date</strong>
                      </td>
                      <td className="text-end text-primary">
                        {projectDetails.startDate}
                      </td>
                    </tr>
                    <tr>
                      <td>
                        <strong>End Date</strong>
                      </td>
                      <td className="text-end text-danger">
                        {projectDetails.endDate}
                      </td>
                    </tr>
                    <tr>
                      <td>
                        <strong>Pricing</strong>
                      </td>
                      <td className="text-end">
                        <h6>{projectDetails.pricing}</h6>
                      </td>
                    </tr>
                    <tr>
                      <td>
                        <strong>Assigned By</strong>
                      </td>
                      <td className="text-end">
                        <ul className="avatar-group justify-content-end">
                          <li className="text-bg-danger h-30 w-30 d-flex-center b-r-50">
                            A
                          </li>
                          <li className="text-bg-dark h-30 w-30 d-flex-center b-r-50">
                            CD
                          </li>
                          <li className="text-bg-warning h-30 w-30 d-flex-center b-r-50">
                            XYZ
                          </li>
                          <li className="text-bg-secondary h-25 w-25 d-flex-center b-r-50">
                            2+
                          </li>
                        </ul>
                      </td>
                    </tr>
                    <tr>
                      <td>
                        <strong>Status</strong>
                      </td>
                      <td className="text-end">
                        <Badge color="light-primary">
                          {projectDetails.status}
                        </Badge>
                      </td>
                    </tr>
                  </tbody>
                </Table>
              </CardBody>
            </Card>

            <Card>
              <CardHeader>
                <h5 className="header-title-text">Project Team</h5>
              </CardHeader>
              <CardBody>
                <div className="project-team-list">
                  {teamMembers.map((member, index) => (
                    <div className="d-flex align-items-center mt-3" key={index}>
                      {member.imgSrc ? (
                        <div
                          className={`bg-primary h-35 w-35 d-flex-center b-r-10 overflow-hidden`}
                        >
                          <img
                            src={member.imgSrc}
                            alt={member.name}
                            className="img-fluid"
                          />
                        </div>
                      ) : (
                        <span
                          className={`${member.color} h-35 w-35 d-flex-center b-r-10`}
                        >
                          {member.initial}
                        </span>
                      )}
                      <div className="flex-grow-1 ps-2">
                        <h6 className="mb-0 fw-medium">{member.name}</h6>
                        <p className="text-muted f-s-13 mb-0 f-w-500">
                          {member.role}
                        </p>
                      </div>
                    </div>
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xxl="6" className=" order-xxl-2">
            <Card>
              <CardHeader>
                <h5>Project Activity</h5>
              </CardHeader>
              <CardBody>
                <ul className="app-timeline-box">
                  {projectActivities.map((activity) => (
                    <li className="timeline-section" key={activity.id}>
                      <div className="timeline-icon">
                        <span
                          className={`text-light-${activity.id === 2 ? "info" : activity.id === 3 ? "success" : activity.id === 4 ? "danger" : "primary"} h-35 w-35 d-flex-center b-r-50`}
                        >
                          {activity.icon || activity.id === 2 ? (
                            <i className="ph-duotone ph-image f-s-18"></i>
                          ) : (
                            activity.icon
                          )}
                        </span>
                      </div>
                      <div className="timeline-content pt-0">
                        <div className="d-flex f-s-16">
                          <p
                            className={`text-${activity.id === 2 ? "info" : activity.id === 3 ? "success" : activity.id === 4 ? "danger" : "primary"} f-s-16 mb-0`}
                          >
                            {activity.user}
                          </p>
                          {activity.badge && (
                            <span className="text-secondary ms-2">
                              {activity.action}{" "}
                              <span className="badge text-outline-primary me-2">
                                {activity.badge}
                              </span>
                            </span>
                          )}
                        </div>
                        <p className="">{activity.time}</p>

                        {activity.images && (
                          <div className="app-timeline-info-text timeline-border-box me-2 ms-0 mt-3 p-3">
                            <Row>
                              {activity.images.map((image, index) => (
                                <Col sm="4" key={index}>
                                  <a
                                    href={image}
                                    className="glightbox img-hover-zoom"
                                    data-glightbox="type: image; zoomable: true;"
                                  >
                                    <img
                                      src={image}
                                      className="w-100 rounded"
                                      alt=""
                                    />
                                  </a>
                                </Col>
                              ))}
                            </Row>
                          </div>
                        )}

                        {activity.feature && (
                          <div className="timeline-border-box me-2 ms-0 mt-3">
                            <h6 className="mb-0">{activity.feature.title}</h6>
                            <p className="mb-4 text-secondary">
                              {activity.feature.description}
                            </p>
                            <span className="badge text-outline-success me-2 timeline-badge">
                              {activity.feature.reactions}
                            </span>
                            <span className="badge text-outline-success me-2">
                              {activity.feature.replies}
                            </span>
                          </div>
                        )}

                        {activity.buttons && (
                          <div>
                            <Button color="danger" className="me-2">
                              Accept
                            </Button>
                            <Button outline color="danger">
                              Reject
                            </Button>
                          </div>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              </CardBody>
              <CardFooter>
                <div className="d-flex">
                  <div className="flex-grow-1">
                    <div className="input-group">
                      <span className="input-group-text ms-2 me-2 b-r-10">
                        <a role="button">
                          <IconMoodSmileBeam size={20} />
                        </a>
                      </span>
                      <Input
                        type="text"
                        className="b-r-6"
                        placeholder="Type a message"
                        aria-label="Recipient's username"
                      />
                      <Button
                        className="btn btn-sm btn-primary ms-2 me-2 b-r-4"
                        type="button"
                      >
                        <IconSend size={20} /> Send
                      </Button>
                    </div>
                  </div>
                  <div className="ms-2 d-none d-sm-block">
                    <a
                      className="text-light-secondary h-35 w-35 d-flex-center b-r-10 ms-1"
                      role="button"
                    >
                      <IconMicrophone size={20} />
                    </a>
                  </div>
                  <div className="d-none d-sm-block">
                    <a
                      className="text-light-secondary h-35 w-35 d-flex-center b-r-10 ms-1"
                      role="button"
                    >
                      <IconCamera size={20} />
                    </a>
                  </div>
                  <div className="d-none d-sm-block">
                    <a
                      className="text-light-secondary h-35 w-35 d-flex-center b-r-10 ms-1"
                      role="button"
                    >
                      <IconPaperclip size={20} />
                    </a>
                  </div>
                  <div>
                    <div className="btn-group dropdown-icon-none d-sm-none">
                      <button
                        className="h-35 w-35 d-flex-center ms-1"
                        onClick={() =>
                          setMobileDropdownOpen(!mobileDropdownOpen)
                        }
                      >
                        <i className="ti ti-dots-vertical"></i>
                      </button>
                      {mobileDropdownOpen && (
                        <ul
                          className="dropdown-menu"
                          data-popper-placement="bottom-start"
                        >
                          <li>
                            <a className="dropdown-item" href="#">
                              <i className="ti ti-microphone"></i>{" "}
                              <span className="f-s-13">Microphone</span>
                            </a>
                          </li>
                          <li>
                            <a className="dropdown-item" href="#">
                              <i className="ti ti-camera-plus"></i>{" "}
                              <span className="f-s-13">Camera</span>
                            </a>
                          </li>
                          <li>
                            <a className="dropdown-item" href="#">
                              <i className="ti ti-paperclip"></i>{" "}
                              <span className="f-s-13">Paperclip</span>
                            </a>
                          </li>
                        </ul>
                      )}
                    </div>
                  </div>
                </div>
              </CardFooter>
            </Card>
          </Col>

          <Col md="6" xxl="3" className="order-md-1 order-xxl-3">
            <Card>
              <CardHeader>
                <h5>About Project</h5>
              </CardHeader>
              <CardBody>
                <div className="mb-3">
                  <h6>Project Description</h6>
                  <p className="text-muted">{projectDetails1.description}</p>
                </div>
                <div className="mb-3">
                  <h6>Task Information</h6>
                  <p className="text-muted">{projectDetails1.taskInfo}</p>
                </div>
                <div className="mb-3">
                  <h6>Background Information</h6>
                  <p className="text-muted">{projectDetails1.backgroundInfo}</p>
                </div>
              </CardBody>
            </Card>

            <Card>
              <CardBody>
                <h5 className="header-title-text">Data Folder & Files</h5>
                <div className="data-list-box mt-3">
                  {projectFiles.map((file, index) => (
                    <div className="filebox" key={index}>
                      <div className="d-flex align-items-center position-relative">
                        <div className="position-absolute start-0">
                          <img
                            src={file.icon}
                            className="w-35 h-35"
                            alt={file.name}
                          />
                        </div>
                        <div className="flex-grow-1 mg-s-40">
                          <h6 className="mb-0">{file.name}</h6>
                          <p className="text-secondary mb-0">{file.count}</p>
                        </div>
                        <p className="file-data text-secondary f-w-500 mb-0">
                          {file.size}
                        </p>
                      </div>
                    </div>
                  ))}
                  <div>
                    <Link
                      href="/apps/file-manager"
                      role="button"
                      className="btn btn-light-primary w-100 rounded"
                    >
                      View More
                    </Link>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ProjectDetails;
