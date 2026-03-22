"use client";
import React, { useState } from "react";
import {
  Container,
  TabContent,
  TabPane,
  Card,
  CardBody,
  Row,
  Col,
  Nav,
  NavItem,
  NavLink,
} from "reactstrap";
import {
  developerteamData,
  marketingteamData,
  teamMembers,
} from "@/Data/Apps/Team/Teamdata";
import Link from "next/link";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  IconBrandFacebook,
  IconBrandPinterest,
  IconBrandTwitter,
  IconBrandWhatsapp,
  IconEdit,
  IconSearch,
  IconStack2,
  IconTrash,
} from "@tabler/icons-react";
import classnames from "classnames";

const Team = () => {
  const [activeTab, setActiveTab] = useState<string>("1");
  const toggleTab = (tab: string) => {
    if (activeTab !== tab) {
      setActiveTab(tab);
    }
  };

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Team"
        title="Apps"
        path={["Team"]}
        Icon={IconStack2}
      />
      <div className="tab-wrapper">
        <Nav tabs className="d-flex align-items-center tab-wrapper tabs">
          <NavItem>
            <NavLink
              className={classnames("tab-link", { active: activeTab === "1" })}
              onClick={() => toggleTab("1")}
            >
              <IconSearch size={18} className={"me-2"} />
              Designer
            </NavLink>
          </NavItem>
          <NavItem>
            <NavLink
              className={classnames("tab-link", { active: activeTab === "2" })}
              onClick={() => toggleTab("2")}
            >
              <IconEdit size={18} className={"me-2"} />
              Development
            </NavLink>
          </NavItem>
          <NavItem>
            <NavLink
              className={classnames("tab-link", { active: activeTab === "3" })}
              onClick={() => toggleTab("3")}
            >
              <IconTrash size={18} className={"me-2"} />
              Marketing
            </NavLink>
          </NavItem>
        </Nav>
      </div>
      <TabContent activeTab={activeTab} className="content-wrapper mt-3">
        <TabPane tabId="1">
          <Card>
            <CardBody>
              <Row>
                {teamMembers.map((member, index: number) => (
                  <Col md={6} xl={4} key={index}>
                    <Card className="team-box-card hover-effect overflow-hidden">
                      <div className="team-box">
                        <img
                          src={member.coverImg}
                          className="card-img-top"
                          alt={member.name}
                        />
                      </div>

                      <div className="team-container">
                        <div className="team-pic">
                          <span className="bg-secondary h-80 w-80 d-flex-center b-r-50 position-relative overflow-hidden">
                            <img
                              src={member.avatarImg}
                              alt={member.name}
                              className="img-fluid b-r-50"
                            />
                          </span>
                        </div>
                      </div>

                      <div className="team-content">
                        <div className="mb-3 mt-3">
                          <h5>{member.name}</h5>
                          <p>{member.position}</p>
                        </div>
                        <div className="team-details">
                          <div className="team-contentbox">
                            <p className="f-w-500">
                              <Link
                                target="_blank"
                                href="/apps/projects-page/projects"
                                className="f-w-600 text-dark"
                              >
                                Projects
                              </Link>
                            </p>
                            <p className="text-center">{member.projects}</p>
                          </div>
                          <div className="team-contentbox">
                            <p className="f-w-500">
                              <Link
                                target="_blank"
                                href="/apps/todo"
                                className="f-w-600 text-dark"
                              >
                                Tasks
                              </Link>
                            </p>
                            <p className="text-center">{member.tasks}</p>
                          </div>
                          <div className="team-contentbox">
                            <p className="f-w-500">
                              <Link
                                target="_blank"
                                href="/apps/projects-page/projects-details"
                                className="f-w-600 text-dark"
                              >
                                Position
                              </Link>
                            </p>
                            <p className="text-center">{member.employeeType}</p>
                          </div>
                        </div>
                        <p className="team-content-list text-muted mb-3">
                          {member.description}
                        </p>
                        <div className="p-2 mb-3">
                          <button
                            type="button"
                            className="btn btn-facebook icon-btn b-r-22 me-2"
                          >
                            <IconBrandFacebook
                              size={18}
                              className="text-white"
                            />
                          </button>
                          <button
                            type="button"
                            className="btn btn-twitter icon-btn b-r-22 me-2"
                          >
                            <IconBrandTwitter
                              size={18}
                              className="text-white"
                            />
                          </button>
                          <button
                            type="button"
                            className="btn btn-pinterest icon-btn b-r-22 me-2"
                          >
                            <IconBrandPinterest
                              size={18}
                              className="text-white"
                            />
                          </button>
                          <button
                            type="button"
                            className="btn btn-whatsapp icon-btn b-r-22 me-2"
                          >
                            <IconBrandWhatsapp
                              size={18}
                              className="text-white"
                            />
                          </button>
                        </div>
                      </div>
                    </Card>
                  </Col>
                ))}
              </Row>
            </CardBody>
          </Card>
        </TabPane>
        <TabPane tabId="2">
          <Card>
            <CardBody>
              <Row>
                {developerteamData.map((member, index) => (
                  <Col md={6} xl={4} key={index}>
                    <Card className="team-box-card hover-effect overflow-hidden">
                      <div className="team-box">
                        <img
                          src={member.coverImg}
                          className="card-img-top"
                          alt={member.name}
                        />
                      </div>

                      <div className="team-container">
                        <div className="team-pic">
                          <span className="bg-secondary h-80 w-80 d-flex-center b-r-50 position-relative overflow-hidden">
                            <img
                              src={member.avatarImg}
                              alt={member.name}
                              className="img-fluid b-r-50"
                            />
                          </span>
                        </div>
                      </div>

                      <div className="team-content">
                        <div className="mb-3 mt-3">
                          <h5>{member.name}</h5>
                          <p>{member.position}</p>
                        </div>
                        <div className="team-details">
                          <div className="team-contentbox">
                            <p className="f-w-500">
                              <Link
                                target="_blank"
                                href="/apps/projects-page/projects"
                                className="f-w-600 text-dark"
                              >
                                Projects
                              </Link>
                            </p>
                            <p className="text-center">{member.projects}</p>
                          </div>
                          <div className="team-contentbox">
                            <p className="f-w-500">
                              <Link
                                target="_blank"
                                href="/apps/todo"
                                className="f-w-600 text-dark"
                              >
                                Tasks
                              </Link>
                            </p>
                            <p className="text-center">{member.tasks}</p>
                          </div>
                          <div className="team-contentbox">
                            <p className="f-w-500">
                              <Link
                                target="_blank"
                                href="/apps/projects-page/projects-details"
                                className="f-w-600 text-dark"
                              >
                                Position
                              </Link>
                            </p>
                            <p className="text-center">{member.position}</p>
                          </div>
                        </div>
                        <p className="team-content-list text-muted mb-3">
                          {member.description}
                        </p>
                        <div className="p-2 mb-3">
                          <a
                            href="https://www.facebook.com"
                            className="btn btn-facebook icon-btn b-r-22 me-2"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <IconBrandFacebook
                              size={18}
                              className="text-white"
                            />
                          </a>
                          <a
                            href="https://twitter.com"
                            className="btn btn-twitter icon-btn b-r-22 me-2"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <IconBrandTwitter
                              size={18}
                              className="text-white"
                            />
                          </a>
                          <a
                            href="https://www.pinterest.com"
                            className="btn btn-pinterest icon-btn b-r-22 me-2"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <IconBrandPinterest
                              size={18}
                              className="text-white"
                            />
                          </a>
                          <a
                            href="https://www.whatsapp.com"
                            className="btn btn-whatsapp icon-btn b-r-22 me-2"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <IconBrandWhatsapp
                              size={18}
                              className="text-white"
                            />
                          </a>
                        </div>
                      </div>
                    </Card>
                  </Col>
                ))}
              </Row>
            </CardBody>
          </Card>
        </TabPane>
        <TabPane tabId="3">
          <Card>
            <CardBody>
              <Row>
                {marketingteamData.map((member, index) => (
                  <Col md={6} xl={4} key={index}>
                    <Card className="team-box-card hover-effect overflow-hidden">
                      <div className="team-box">
                        <img
                          src={member.coverImg}
                          className="card-img-top"
                          alt={member.name}
                        />
                      </div>

                      <div className="team-container">
                        <div className="team-pic">
                          <span className="bg-secondary h-80 w-80 d-flex-center b-r-50 position-relative overflow-hidden">
                            <img
                              src={member.avatarImg}
                              alt={member.name}
                              className="img-fluid b-r-50"
                            />
                          </span>
                        </div>
                      </div>

                      <div className="team-content">
                        <div className="mb-3 mt-3">
                          <h5>{member.name}</h5>
                          <p>{member.role}</p>
                        </div>
                        <div className="team-details">
                          <div className="team-contentbox">
                            <p className="f-w-500">
                              <Link
                                target="_blank"
                                href="/apps/projects-page/projects"
                                className="f-w-600 text-dark"
                              >
                                Projects
                              </Link>
                            </p>
                            <p className="text-center">{member.projects}</p>
                          </div>
                          <div className="team-contentbox">
                            <p className="f-w-500">
                              <Link
                                target="_blank"
                                href="/apps/todo"
                                className="f-w-600 text-dark"
                              >
                                Tasks
                              </Link>
                            </p>
                            <p className="text-center">{member.tasks}</p>
                          </div>
                          <div className="team-contentbox">
                            <p className="f-w-500">
                              <Link
                                target="_blank"
                                href="/apps/projects-page/projects-details"
                                className="f-w-600 text-dark"
                              >
                                Position
                              </Link>
                            </p>
                            <p className="text-center">{member.position}</p>
                          </div>
                        </div>
                        <p className="team-content-list text-muted mb-3">
                          {member.description}
                        </p>
                        <div className="p-2 mb-3">
                          <a
                            href="https://www.facebook.com"
                            className="btn btn-facebook icon-btn b-r-22 me-2"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <IconBrandFacebook
                              size={18}
                              className="text-white"
                            />
                          </a>
                          <a
                            href="https://twitter.com"
                            className="btn btn-twitter icon-btn b-r-22 me-2"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <IconBrandTwitter
                              size={18}
                              className="text-white"
                            />
                          </a>
                          <a
                            href="https://www.pinterest.com"
                            className="btn btn-pinterest icon-btn b-r-22 me-2"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <IconBrandPinterest
                              size={18}
                              className="text-white"
                            />
                          </a>
                          <a
                            href="https://www.whatsapp.com"
                            className="btn btn-whatsapp icon-btn b-r-22 me-2"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <IconBrandWhatsapp
                              size={18}
                              className="text-white"
                            />
                          </a>
                        </div>
                      </div>
                    </Card>
                  </Col>
                ))}
              </Row>
            </CardBody>
          </Card>
        </TabPane>
      </TabContent>
    </Container>
  );
};

export default Team;
