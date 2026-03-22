"use client";

import React, { useRef, useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  CardFooter,
  Progress,
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
  Badge,
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
  Modal,
  ModalHeader,
  ModalBody,
  ModalFooter,
  Button,
  FormGroup,
  Label,
  Input,
  Col,
  Row,
  Container,
} from "reactstrap";
import classnames from "classnames";
import { IconPlus, IconStack2 } from "@tabler/icons-react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Image from "next/image";
import Link from "next/link";
import { submitProject } from "../action";

interface FormData {
  projectName: string;
  image: File | null;
  startDate: string;
  endDate: string;
  pricing: string;
  description: string;
}

interface Project {
  id: number;
  title: string;
  subtitle: string;
  logo: string;
  startDate: string;
  endDate: string;
  price: string;
  description: string;
  status: string;
  progress: number;
  progressColor:
    | "primary"
    | "success"
    | "danger"
    | "warning"
    | "info"
    | "secondary";
  members: number;
  avatars: string[];
  moreMembers: number;
}

interface ProjectCardData {
  logo: string;
  title: string;
  subtitle: string;
  startDate: string;
  endDate: string;
  pricing: string;
  description: string;
  badgeText: string;
  badgeClass: string;
  progress: number;
  progressClass: string;
  members: number;
  avatars: string[];
  moreLabel: string;
}

interface ProjectData {
  id: number;
  logo: string;
  title: string;
  subtitle: string;
  startDate: string;
  endDate: string;
  pricing: string;
  description: string;
  progress: number;
  progressColor: string;
  badgeColor: string;
  membersCount: number;
  memberAvatars: string[];
}

const projects: Project[] = [
  {
    id: 1,
    title: "Web Designing",
    subtitle: "Admin",
    logo: "/images/icons/logo1.png",
    startDate: "2024-09-24",
    endDate: "2024-12-05",
    price: "$10k",
    description:
      "I am a keen, hardworking, reliable and excellent timekeeper. I am a bright and receptive person",
    status: "Progress",
    progress: 50,
    progressColor: "primary",
    members: 20,
    avatars: [
      "/images/avatar/4.png",
      "/images/avatar/1.png",
      "/images/avatar/2.png",
      "/images/avatar/3.png",
    ],
    moreMembers: 5,
  },
  {
    id: 2,
    title: "Designing",
    subtitle: "Prototyping",
    logo: "/images/icons/logo2.png",
    startDate: "2024-02-03",
    endDate: "2024-04-05",
    price: "$280",
    description:
      "I am a keen, hardworking, reliable and excellent timekeeper. I am a bright and receptive person",
    status: "Completed",
    progress: 100,
    progressColor: "success",
    members: 10,
    avatars: ["/images/avatar/4.png", "/images/avatar/1.png"],
    moreMembers: 5,
  },
  {
    id: 3,
    title: "Designing",
    subtitle: "Dashboard",
    logo: "/images/icons/logo3.png",
    startDate: "2024-10-10",
    endDate: "2024-02-16",
    price: "$100k",
    description:
      "I am a keen, hard working, reliable and excellent time keeper. I am a bright and receptive person",
    status: "New",
    progress: 0,
    progressColor: "danger",
    members: 25,
    avatars: [
      "/images/avatar/4.png",
      "/images/avatar/1.png",
      "/images/avatar/5.png",
    ],
    moreMembers: 10,
  },
  {
    id: 4,
    title: "Web Development",
    subtitle: "Weather Application",
    logo: "/images/icons/logo4.png",
    startDate: "2024-06-16",
    endDate: "2024-01-01",
    price: "$400k",
    description:
      "I am a keen, hard working, reliable and excellent time keeper. I am a bright and receptive person",
    status: "Progress",
    progress: 40,
    progressColor: "primary",
    members: 34,
    avatars: [
      "/images/avatar/4.png",
      "/images/avatar/1.png",
      "/images/avatar/5.png",
    ],
    moreMembers: 10,
  },
  {
    id: 5,
    title: "Web Design",
    subtitle: "Application Designing",
    logo: "/images/icons/logo5.png",
    startDate: "2024-06-16",
    endDate: "2024-01-01",
    price: "$200k",
    description:
      "I am a keen, hard working, reliable and excellent time keeper. I am a bright and receptive person",
    status: "Progress",
    progress: 50,
    progressColor: "primary",
    members: 15,
    avatars: [
      "/images/avatar/4.png",
      "/images/avatar/1.png",
      "/images/avatar/5.png",
    ],
    moreMembers: 10,
  },
  {
    id: 6,
    title: "Designing",
    subtitle: "Logo Designing",
    logo: "/images/icons/logo6.png",
    startDate: "2024-07-16",
    endDate: "2024-09-26",
    price: "$400",
    description:
      "I am a keen, hard working, reliable and excellent time keeper. I am a bright and receptive person",
    status: "Progress",
    progress: 75,
    progressColor: "success",
    members: 5,
    avatars: [
      "/images/avatar/4.png",
      "/images/avatar/1.png",
      "/images/avatar/5.png",
    ],
    moreMembers: 2,
  },
];

const projectCards: ProjectCardData[] = [
  {
    logo: "/images/icons/logo1.png",
    title: "Web Designing",
    subtitle: "Admin",
    startDate: "2024-09-24",
    endDate: "2024-12-05",
    pricing: "$10k",
    description:
      "I am a keen, hard working, reliable and excellent time keeper. I am a bright and receptive person",
    badgeText: "Progress",
    badgeClass: "text-light-primary",
    progress: 50,
    progressClass: "bg-primary",
    members: 20,
    avatars: [
      "/images/avatar/4.png",
      "/images/avatar/1.png",
      "/images/avatar/2.png",
      "/images/avatar/3.png",
    ],
    moreLabel: "5+",
  },
  {
    logo: "/images/icons/logo3.png",
    title: "Designing",
    subtitle: "Dashboard",
    startDate: "2024-10-10",
    endDate: "2024-02-16",
    pricing: "$100k",
    description:
      "I am a keen, hard working, reliable and excellent time keeper. I am a bright and receptive person",
    badgeText: "New",
    badgeClass: "text-light-secondary",
    progress: 0,
    progressClass: "bg-danger",
    members: 25,
    avatars: [
      "/images/avatar/4.png",
      "/images/avatar/1.png",
      "/images/avatar/5.png",
    ],
    moreLabel: "10+",
  },
  {
    logo: "/images/icons/logo6.png",
    title: "Designing",
    subtitle: "Logo Designing",
    startDate: "2024-07-16",
    endDate: "2024-09-26",
    pricing: "$400",
    description:
      "I am a keen, hard working, reliable and excellent time keeper. I am a bright and receptive person",
    badgeText: "Progress",
    badgeClass: "text-light-success",
    progress: 75,
    progressClass: "bg-success",
    members: 5,
    avatars: [
      "/images/avatar/4.png",
      "/images/avatar/1.png",
      "/images/avatar/5.png",
    ],
    moreLabel: "2+",
  },
];

const project: ProjectData[] = [
  {
    id: 1,
    logo: "/images/icons/logo4.png",
    title: "Web Development",
    subtitle: "Weather Application",
    startDate: "2024-06-16",
    endDate: "2024-01-01",
    pricing: "$400k",
    description:
      "I am a keen, hard working, reliable and excellent time keeper. I am a bright and receptive person",
    progress: 90,
    progressColor: "success",
    badgeColor: "light-success",
    membersCount: 34,
    memberAvatars: [
      "/images/avatar/4.png",
      "/images/avatar/1.png",
      "/images/avatar/5.png",
    ],
  },
  {
    id: 2,
    logo: "/images/icons/logo5.png",
    title: "Web Design",
    subtitle: "Application Designing",
    startDate: "2024-06-16",
    endDate: "2024-01-01",
    pricing: "$200k",
    description:
      "I am a keen, hard working, reliable and excellent time keeper. I am a bright and receptive person",
    progress: 50,
    progressColor: "primary",
    badgeColor: "light-primary",
    membersCount: 15,
    memberAvatars: [
      "/images/avatar/4.png",
      "/images/avatar/1.png",
      "/images/avatar/5.png",
    ],
  },
];

const ProjectTabs: React.FC = () => {
  const [dropdownOpen, setDropdownOpen] = useState<Record<string, boolean>>({});
  const [activeTab, setActiveTab] = useState<string>("1");
  const [modalOpen, setModalOpen] = useState<boolean>(false);
  const [formData, setFormData] = useState<FormData>({
    projectName: "",
    image: null,
    startDate: "",
    endDate: "",
    pricing: "",
    description: "",
  });
  const formRef = useRef<HTMLFormElement>(null);

  const toggleDropdown = (cardId: number | string) => {
    setDropdownOpen((prev) => ({
      ...prev,
      [cardId]: !prev[cardId],
    }));
  };

  const toggleTab = (tab: string) => {
    if (activeTab !== tab) {
      setActiveTab(tab);
    }
  };

  const toggleModal = () => {
    setModalOpen(!modalOpen);
  };

  const handleInputChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>
  ) => {
    const { name, value } = e.target;
    setFormData((prev) => ({
      ...prev,
      [name]: value,
    }));
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      setFormData((prev) => ({
        ...prev,
        image: e.target.files![0] || null,
      }));
    }
  };

  const handleSubmit = () => {
    if (formRef.current) {
      formRef.current.requestSubmit();
      toggleModal();
    }
  };
  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Project"
        title="Apps"
        path={["Project App", "Project"]}
        Icon={IconStack2}
      />
      <Row>
        <Col xs={12}>
          <div className="tab-wrapper mb-3">
            <Nav tabs className="d-flex align-items-center tab-wrapper tabs">
              <NavItem>
                <NavLink
                  className={classnames("tab-link", {
                    active: activeTab === "1",
                  })}
                  onClick={() => toggleTab("1")}
                >
                  All Project
                </NavLink>
              </NavItem>
              <NavItem>
                <NavLink
                  className={classnames("tab-link", {
                    active: activeTab === "2",
                  })}
                  onClick={() => toggleTab("2")}
                >
                  Designing Project
                </NavLink>
              </NavItem>
              <NavItem>
                <NavLink
                  className={classnames("tab-link", {
                    active: activeTab === "3",
                  })}
                  onClick={() => toggleTab("3")}
                >
                  Development Project
                </NavLink>
              </NavItem>
              <div className="ms-auto text-end">
                <Button
                  color="primary"
                  className="w-45 h-45 icon-btn b-r-10 m-2"
                  onClick={toggleModal}
                >
                  <IconPlus size={18} />
                </Button>
              </div>
            </Nav>
          </div>

          <TabContent activeTab={activeTab}>
            <TabPane tabId="1">
              <div className="row">
                {projects.map((project) => (
                  <div
                    className="col-md-6 col-xl-4 project-card"
                    key={project.id}
                  >
                    <Card className="hover-effect">
                      <CardHeader>
                        <div className="d-flex align-items-center">
                          <div className="h-40 w-40 d-flex-center b-r-50 overflow-hidden">
                            <Image
                              alt={project.title}
                              src={project.logo}
                              width={40}
                              height={40}
                              className="img-fluid"
                            />
                          </div>
                          <Link
                            href="/apps/projects-page/projects"
                            passHref
                            legacyBehavior
                          >
                            <a className="flex-grow-1 ps-2">
                              <h6 className="m-0 text-dark f-w-600">
                                {project.title}
                              </h6>
                              <div className="text-muted f-s-14 f-w-500">
                                {project.subtitle}
                              </div>
                            </a>
                          </Link>

                          <Dropdown
                            isOpen={dropdownOpen[project.id] || false}
                            toggle={() => toggleDropdown(project.id)}
                          >
                            <DropdownToggle
                              tag="button"
                              className="bg-none border-0"
                            >
                              <i className="ti ti-dots-vertical text-dark"></i>
                            </DropdownToggle>
                            <DropdownMenu end>
                              <DropdownItem>
                                <i className="ti ti-edit text-success me-2"></i>{" "}
                                Edit
                              </DropdownItem>
                              <DropdownItem className="delete-button">
                                <i className="ti ti-trash text-danger me-2"></i>{" "}
                                Delete
                              </DropdownItem>
                            </DropdownMenu>
                          </Dropdown>
                        </div>
                      </CardHeader>
                      <CardBody>
                        <div className="d-flex">
                          <div>
                            <h6 className="text-dark f-s-14">
                              Start Date :{" "}
                              <span className="text-success">
                                {project.startDate}
                              </span>
                            </h6>
                            <h6 className="text-dark f-s-14">
                              End Date :{" "}
                              <span className="text-danger">
                                {project.endDate}
                              </span>
                            </h6>
                          </div>
                          <div className="flex-grow-1 text-end">
                            <p className="f-w-500 text-secondary">Pricing</p>
                            <h6 className="f-w-600">{project.price}</h6>
                          </div>
                        </div>
                        <p className="text-muted f-s-14 text-secondary txt-ellipsis-2">
                          {project.description}
                        </p>
                        <div className="text-end mb-2">
                          <Badge color={`light-${project.progressColor}`}>
                            {project.status}
                          </Badge>
                        </div>
                        <Progress
                          value={project.progress}
                          color={project.progressColor}
                        >
                          {project.progress}%
                        </Progress>
                      </CardBody>
                      <CardFooter>
                        <div className="row align-items-center">
                          <div className="col-6">
                            <span className="text-dark f-w-600">
                              <i className="ti ti-brand-wechat f-s-18 me-1"></i>{" "}
                              {project.members} Members
                            </span>
                          </div>
                          <div className="col-6">
                            <ul className="avatar-group float-end breadcrumb-start">
                              {project.avatars.map((avatar, index) => (
                                <li
                                  key={index}
                                  className={`h-30 w-30 d-flex-center b-r-50 ${index === 0 ? "text-bg-danger" : "text-bg-success"} b-2-light position-relative`}
                                  title="Sabrina Torres"
                                >
                                  <Image
                                    alt={`Team member ${index + 1}`}
                                    src={avatar}
                                    width={30}
                                    height={30}
                                    className="img-fluid b-r-50 overflow-hidden"
                                  />
                                </li>
                              ))}
                              {project.moreMembers > 0 && (
                                <li
                                  className="text-bg-primary h-25 w-25 d-flex-center b-r-50"
                                  title={`${project.moreMembers} More`}
                                >
                                  {project.moreMembers}+
                                </li>
                              )}
                            </ul>
                          </div>
                        </div>
                      </CardFooter>
                    </Card>
                  </div>
                ))}
              </div>
            </TabPane>
            <TabPane tabId="2">
              <div className="row">
                {projectCards.map((card, idx) => (
                  <div className="col-md-6 col-xl-4 project-card" key={idx}>
                    <div className="card hover-effect">
                      <div className="card-header">
                        <div className="d-flex align-items-center">
                          <div className="h-40 w-40 d-flex-center b-r-50 overflow-hidden">
                            <img src={card.logo} alt="" className="img-fluid" />
                          </div>
                          <Link
                            className="flex-grow-1 ps-2"
                            href="/apps/projects-page/projects-details"
                            target="_blank"
                            rel="noreferrer"
                          >
                            <h6 className="m-0 text-dark f-w-600">
                              {card.title}
                            </h6>
                            <div className="text-muted f-s-14 f-w-500">
                              {card.subtitle}
                            </div>
                          </Link>
                          <div className="dropdown">
                            <Dropdown
                              isOpen={dropdownOpen[`card-${idx}`] || false}
                              toggle={() => toggleDropdown(`card-${idx}`)}
                            >
                              <DropdownToggle
                                tag="button"
                                className="bg-none border-0"
                              >
                                <i className="ti ti-dots-vertical text-dark"></i>
                              </DropdownToggle>
                              <DropdownMenu end>
                                <DropdownItem>
                                  <i className="ti ti-edit text-success me-2"></i>{" "}
                                  Edit
                                </DropdownItem>
                                <DropdownItem className="delete-button">
                                  <i className="ti ti-trash text-danger me-2"></i>{" "}
                                  Delete
                                </DropdownItem>
                              </DropdownMenu>
                            </Dropdown>
                          </div>
                        </div>
                      </div>
                      <div className="card-body">
                        <div className="d-flex">
                          <div>
                            <h6 className="text-dark f-s-14 f-w-500">
                              Start Date:{" "}
                              <span className="text-success">
                                {card.startDate}
                              </span>
                            </h6>
                            <h6 className="text-dark f-s-14 f-w-500">
                              End Date:{" "}
                              <span className="text-danger">
                                {card.endDate}
                              </span>
                            </h6>
                          </div>
                          <div className="flex-grow-1 text-end">
                            <p className="f-w-500 text-secondary">Pricing</p>
                            <h6 className="f-w-600">{card.pricing}</h6>
                          </div>
                        </div>
                        <p className="text-muted f-s-14 text-secondary txt-ellipsis-2">
                          {card.description}
                        </p>
                        <div className="text-end mb-2">
                          <span className={`badge ${card.badgeClass}`}>
                            {card.badgeText}
                          </span>
                        </div>
                        <div
                          className="progress w-100"
                          role="progressbar"
                          aria-valuenow={card.progress}
                          aria-valuemin={0}
                          aria-valuemax={100}
                        >
                          <div
                            className={`progress-bar ${card.progressClass}`}
                            style={{ width: `${card.progress}%` }}
                          >
                            {card.progress}%
                          </div>
                        </div>
                      </div>
                      <div className="card-footer">
                        <div className="row">
                          <div className="col-6">
                            <span className="text-dark f-w-600">
                              <i className="ti ti-brand-wechat"></i>{" "}
                              {card.members} Members
                            </span>
                          </div>
                          <div className="col-6">
                            <ul className="avatar-group float-end breadcrumb-start">
                              {card.avatars.map((src, i) => (
                                <li
                                  key={i}
                                  className="h-25 w-25 d-flex-center b-r-50 text-bg-success b-2-light position-relative"
                                >
                                  <img
                                    src={src}
                                    alt={`Team member ${i + 1}`}
                                    className="img-fluid b-r-50 overflow-hidden"
                                  />
                                </li>
                              ))}
                              <li className="text-bg-primary h-25 w-25 d-flex-center b-r-50">
                                {card.moreLabel}
                              </li>
                            </ul>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </TabPane>
            <TabPane tabId="3">
              <div className="row">
                {project.map((project) => (
                  <div
                    className="col-md-6 col-xl-4 project-card"
                    key={project.id}
                  >
                    <Card className="hover-effect">
                      <CardHeader>
                        <div className="d-flex align-items-center">
                          <div className="h-40 w-40 d-flex-center b-r-50 overflow-hidden">
                            <img
                              alt=""
                              className="img-fluid"
                              src={project.logo}
                            />
                          </div>
                          <Link
                            className="flex-grow-1 ps-2"
                            href="/apps/projects-page/projects-details"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <h6 className="m-0 text-dark f-w-600">
                              {project.title}
                            </h6>
                            <div className="text-muted f-s-14 f-w-500">
                              {project.subtitle}
                            </div>
                          </Link>
                          <Dropdown
                            isOpen={dropdownOpen[project.id] || false}
                            toggle={() => toggleDropdown(project.id)}
                          >
                            <DropdownToggle
                              tag="button"
                              className="bg-none border-0"
                            >
                              <i className="ti ti-dots-vertical text-dark" />
                            </DropdownToggle>
                            <DropdownMenu end>
                              <DropdownItem>
                                <i className="ti ti-edit text-success me-1" />{" "}
                                Edit
                              </DropdownItem>
                              <DropdownItem className="delete-button">
                                <i className="ti ti-trash text-danger me-1" />{" "}
                                Delete
                              </DropdownItem>
                            </DropdownMenu>
                          </Dropdown>
                        </div>
                      </CardHeader>
                      <CardBody>
                        <div className="d-flex">
                          <div>
                            <h6 className="text-dark f-s-14 f-w-500">
                              Start Date:{" "}
                              <span className="text-success">
                                {project.startDate}
                              </span>
                            </h6>
                            <h6 className="text-dark f-s-14 f-w-500">
                              End Date:{" "}
                              <span className="text-danger">
                                {project.endDate}
                              </span>
                            </h6>
                          </div>
                          <div className="flex-grow-1 text-end">
                            <p className="f-w-500 text-secondary">Pricing</p>
                            <h6 className="f-w-600">{project.pricing}</h6>
                          </div>
                        </div>
                        <p className="text-muted f-s-14 text-secondary txt-ellipsis-2">
                          {project.description}
                        </p>
                        <div className="text-end mb-2">
                          <Badge color={project.badgeColor}>Progress</Badge>
                        </div>
                        <Progress
                          color={project.progressColor}
                          value={project.progress}
                        >
                          {project.progress}%
                        </Progress>
                      </CardBody>
                      <CardFooter>
                        <div className="row">
                          <div className="col-6">
                            <span className="text-dark f-w-600">
                              <i className="ti ti-brand-wechat"></i>{" "}
                              {project.membersCount} Members
                            </span>
                          </div>
                          <div className="col-6">
                            <ul className="avatar-group float-end breadcrumb-start">
                              {project.memberAvatars.map((avatar, idx) => (
                                <li
                                  key={idx}
                                  className="h-25 w-25 d-flex-center b-r-50 position-relative"
                                  title="Sabrina Torres"
                                >
                                  <img
                                    alt=""
                                    className="img-fluid b-r-50 overflow-hidden"
                                    src={avatar}
                                  />
                                </li>
                              ))}
                              <li
                                className="text-bg-primary h-25 w-25 d-flex-center b-r-50"
                                title="5 More"
                              >
                                10+
                              </li>
                            </ul>
                          </div>
                        </div>
                      </CardFooter>
                    </Card>
                  </div>
                ))}
              </div>
            </TabPane>
          </TabContent>
        </Col>
      </Row>

      <Modal isOpen={modalOpen} toggle={toggleModal}>
        <ModalHeader toggle={toggleModal}>Add Project</ModalHeader>
        <ModalBody>
          <form action={submitProject} className="app-form" ref={formRef}>
            <Row>
              <Col xs="12">
                <FormGroup>
                  <Label for="projectName">Project Name</Label>
                  <Input
                    type="text"
                    name="projectName"
                    id="projectName"
                    placeholder="Designing"
                    value={formData.projectName}
                    onChange={handleInputChange}
                  />
                </FormGroup>
              </Col>
              <Col xs="12">
                <FormGroup>
                  <Label>Image</Label>
                  <Input
                    name="image"
                    type="file"
                    className="file_upload"
                    onChange={handleFileChange}
                  />
                </FormGroup>
              </Col>
              <Col xs="12" lg={6}>
                <FormGroup>
                  <Label for="startDate">Start Date</Label>
                  <Input
                    type="date"
                    name="startDate"
                    id="startDate"
                    value={formData.startDate}
                    onChange={handleInputChange}
                  />
                </FormGroup>
              </Col>
              <Col xs="12" lg={6}>
                <FormGroup>
                  <Label for="endDate">End Date</Label>
                  <Input
                    type="date"
                    name="endDate"
                    id="endDate"
                    value={formData.endDate}
                    onChange={handleInputChange}
                  />
                </FormGroup>
              </Col>
              <Col xs="12">
                <FormGroup>
                  <Label for="pricing">Pricing</Label>
                  <Input
                    type="text"
                    name="pricing"
                    id="pricing"
                    placeholder="$10k"
                    value={formData.pricing}
                    onChange={handleInputChange}
                  />
                </FormGroup>
              </Col>
              <Col xs="12">
                <FormGroup>
                  <Label for="description">Project Description</Label>
                  <Input
                    type="textarea"
                    name="description"
                    id="description"
                    placeholder="Enter Description"
                    rows="5"
                    value={formData.description}
                    onChange={handleInputChange}
                  />
                </FormGroup>
              </Col>
            </Row>
          </form>
        </ModalBody>
        <ModalFooter>
          <Button color="secondary" onClick={toggleModal}>
            Close
          </Button>
          <Button color="primary" onClick={handleSubmit}>
            Save changes
          </Button>
        </ModalFooter>
      </Modal>
    </Container>
  );
};

export default ProjectTabs;
