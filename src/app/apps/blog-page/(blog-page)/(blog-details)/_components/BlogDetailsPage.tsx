"use client";
import React, { useEffect, useState } from "react";
import "glightbox/dist/css/glightbox.min.css";
import {
  Card,
  CardBody,
  CardHeader,
  Button,
  Input,
  Form,
  FormGroup,
  Label,
  Row,
  Col,
  Badge,
  ListGroup,
  ListGroupItem,
  CardImg,
  Container,
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
} from "reactstrap";
import Link from "next/link";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  IconBrandFacebook,
  IconBrandGithub,
  IconBrandHipchat,
  IconSend,
  IconShare,
  IconStack2,
  IconThumbUp,
} from "@tabler/icons-react";
import dynamic from "next/dynamic";
import {
  blogDetailsCategories,
  blogTags,
  popularPosts,
  relatedPosts,
} from "@/Data/Apps/Blog/BlogDetails";
import { subscribeNewsletter } from "../action";

const GLightbox = dynamic(
  async () => {
    const mod = await import("glightbox");
    return () => {
      useEffect(() => {
        const lightbox = mod.default({ selector: ".glightbox" });

        return () => {
          lightbox.destroy();
        };
      }, []);

      return null;
    };
  },
  { ssr: false }
);

const BlogDetailsPage = () => {
  const [email, setEmail] = useState("");
  const [dropdownOpen, setDropdownOpen] = useState<{ [key: string]: boolean }>(
    {}
  );

  const toggleDropdown = (id: string) => {
    setDropdownOpen((prev) => ({
      ...prev,
      [id]: !prev[id],
    }));
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Blog Details"
          title="Apps"
          path={["Blog Page", "Blog Details"]}
          Icon={IconStack2}
        />
        <Row>
          <Col lg="8" xxl="9">
            <div className="card">
              <CardBody>
                {/* Video and Images Section */}
                <div className="post-div mb-3">
                  <Row>
                    <Col xs="12">
                      <video controls className="w-100 h-400 rounded">
                        <source
                          src="/images/blog/video1.mp4"
                          type="video/mp4"
                        />
                        <source src="movie.ogg" type="video/ogg" />
                        Your browser does not support the video tag.
                      </video>
                    </Col>
                    {["09", "05", "08", "04"].map((img, index) => (
                      <Col xs="6" sm="3" key={index}>
                        <a
                          href={`/images/blog/${img}.jpg`}
                          className="glightbox img-fluid"
                          data-glightbox="type: image;"
                        >
                          <img
                            src={`/images/blog/${img}.jpg`}
                            className="img-fluid rounded"
                            alt="blog"
                          />
                        </a>
                      </Col>
                    ))}
                  </Row>
                </div>

                {/* Blog Content */}
                <h5 className="mb-3 text-dark fw-bold">
                  Adjust your focus when life gets blurry.
                </h5>

                <div className="mb-3 text-secondary">
                  <p>
                    Photography is the art, application, and practice of
                    creating images by recording light...
                  </p>
                  <p>
                    The word Photography literally means &#39;drawing with
                    light&#39;...
                  </p>

                  <Card className="card-light-secondary shadow-none my-3">
                    <CardBody>
                      <i className="ti ti-quote icon-bg"></i>
                      <p className="mb-2 text-dark fw-bold">
                        “I never stay in one country more than three months...”
                      </p>
                      <h6 className="text-secondary text-end">
                        <cite>- Josef Koudelka.</cite>
                      </h6>
                    </CardBody>
                  </Card>

                  <p className="mb-4">
                    In 1826, Phosphoric Niece first managed to fix an image...
                  </p>

                  {/* Photographer List + Image */}
                  <Row className="mb-3">
                    <Col md="6">
                      <h5 className="mb-3 text-dark fw-semibold">
                        List of photographers
                      </h5>
                      <ul className="blog-list">
                        {[
                          [
                            "Charlotte Abram (born 1993)",
                            "photographer and filmmaker",
                          ],
                          ["Jennifer Des (born 1975)", "photographer"],
                          [
                            "Nathalie Gasses (born 1964)",
                            "writer, photographer",
                          ],
                          [
                            "Germaine Van Paras (1893 - 1983)",
                            "film director, photographer, educator",
                          ],
                          [
                            "Katrin Vermeer (born 1979)",
                            "photographer, filmmaker",
                          ],
                          [
                            "Stephanie Windings-rate (1939 - 2019)",
                            "artistic portrait and animal photographer",
                          ],
                          [
                            "Claudia Andujar (born 1931)",
                            "Swiss-born Brazilian photographer and photojournalist",
                          ],
                          [
                            "Brigida Baltar (born 1959)",
                            "visual artist and photographer",
                          ],
                          ["Alice Della (born 1987)", "model, photographer"],
                        ].map(([name, desc], i) => (
                          <li key={i}>
                            <span className="text-dark fw-semibold">
                              {name}
                            </span>{" "}
                            - {desc}
                          </li>
                        ))}
                      </ul>
                    </Col>
                    <Col md="6">
                      <a
                        href="/images/blog/03.jpg"
                        className="glightbox"
                        data-glightbox="type: image; zoomable: true;"
                      >
                        <img
                          src="/images/blog/03.jpg"
                          className="w-100 rounded"
                          alt="photographer"
                        />
                      </a>
                    </Col>
                  </Row>

                  <p>
                    The commercial introduction of digital cameras in the
                    1990s...
                  </p>
                </div>

                {/* Author Info + Actions */}
                <div className="app-divider-v mb-2"></div>
                <div className="d-flex align-items-center flex-wrap">
                  <div className="h-50 w-50 d-flex-center b-r-10 overflow-hidden">
                    <img
                      src="/images/avatar/9.png"
                      alt="avatar"
                      className="bg-danger img-fluid"
                    />
                  </div>
                  <div className="flex-grow-1 ps-2 me-2">
                    <h6 className="mb-0 fw-medium text-dark">Bette Hagenes</h6>
                    <p className="text-muted font-size-12 mb-0">26 Nov,2022</p>
                  </div>
                  <div>
                    <Button color="link" className="btn-sm icon-btn b-r-5">
                      <IconThumbUp size={20} />
                    </Button>
                    <Button color="link" className="btn-sm icon-btn b-r-5">
                      <IconBrandHipchat size={20} />
                    </Button>
                    <Button color="link" className="btn-sm icon-btn b-r-5">
                      <IconShare size={20} />
                    </Button>
                  </div>
                </div>
              </CardBody>
            </div>

            {/* Comments Section */}
            <Card>
              <CardHeader>
                <h5>Comments</h5>
              </CardHeader>
              <CardBody>
                {[1, 2].map((_, idx) => (
                  <div className="blogcomment-box mb-3" key={idx}>
                    <div className="d-flex justify-content-between">
                      <div className="h-40 w-40 d-flex-center b-r-10 overflow-hidden comment-img">
                        <img
                          src="/images/avatar/4.png"
                          alt="avatar"
                          className="bg-warning img-fluid"
                        />
                      </div>
                      <div className="commentdiv">
                        <h6 className="mb-0 text-dark fw-semibold">
                          Bette Hagenes{" "}
                          <span className="text-muted fs-12">1 min</span>
                        </h6>
                        <p className="text-muted mb-0">
                          {idx === 0 ? (
                            <>
                              “Photography is the only language that can be
                              understood anywhere in the world.”{" "}
                              <span className="fs-6 d-inline-block text-secondary">
                                <cite>- Bruno Barbey</cite>
                              </span>
                            </>
                          ) : (
                            <>
                              You&#39;re such a talented person with the camera.
                              I appreciate your work...
                            </>
                          )}
                        </p>
                        {idx === 0 && (
                          <>
                            <Badge color="light-secondary">
                              lenora@gmail.com
                            </Badge>{" "}
                            <Badge color="dark">#beautiful</Badge>
                          </>
                        )}
                      </div>
                      <div>
                        <div className="btn-group dropdown-icon-none">
                          <Dropdown
                            isOpen={dropdownOpen[`comment-${idx}`] || false}
                            toggle={() => toggleDropdown(`comment-${idx}`)}
                          >
                            <DropdownToggle className="icon-btn p-2 bg-transparent border-0">
                              <i className="ti ti-dots-vertical"></i>
                            </DropdownToggle>
                            <DropdownMenu>
                              <DropdownItem>
                                <i className="ti ti-share me-2"></i> Share
                              </DropdownItem>
                              <DropdownItem>
                                <i className="ti ti-edit me-2"></i> Edit
                              </DropdownItem>
                              <DropdownItem>
                                <i className="ti ti-trash me-2"></i> Delete
                              </DropdownItem>
                            </DropdownMenu>
                          </Dropdown>
                        </div>
                      </div>
                    </div>
                  </div>
                ))}

                {/* Leave A Comment */}
                <h5 className="mb-3">Leave A Comment</h5>
                <Form className="app-form" id="id1">
                  <Row>
                    <Col md={12}>
                      <FormGroup>
                        <Input
                          type="textarea"
                          rows={3}
                          placeholder="Enter Your Comment"
                        />
                      </FormGroup>
                    </Col>
                    <Col md={6}>
                      <FormGroup>
                        <Input type="text" placeholder="Enter Your Name" />
                      </FormGroup>
                    </Col>
                    <Col md={6}>
                      <FormGroup>
                        <Input type="email" placeholder="Enter Your Email" />
                      </FormGroup>
                    </Col>
                    <Col>
                      <div className="text-end">
                        <Button color="primary">
                          <IconSend size={18} /> Comment
                        </Button>
                      </div>
                    </Col>
                  </Row>
                </Form>
              </CardBody>
            </Card>

            {/* Related Posts */}
            <h5 className="mb-3 mt-4">Related Posts</h5>
            <Row>
              {relatedPosts.map((post, idx) => (
                <Col md="6" xxl="4" key={idx}>
                  <Card className="blog-card overflow-hidden">
                    <a
                      href={`/images/blog/${post.img}`}
                      className="glightbox img-hover-zoom"
                      data-glightbox="type: image; zoomable: true;"
                    >
                      <CardImg
                        top
                        src={`/images/blog/${post.img}`}
                        alt="blog"
                      />
                    </a>
                    <div className="tag-container">
                      <Badge>{post.tag}</Badge>
                    </div>
                    <CardBody>
                      <p className="text-body-secondary">
                        <i className="ti ti-calendar-due"></i> {post.date}
                      </p>
                      <Link
                        href="/apps/blog-page/blog-details"
                        className="bloglink"
                      >
                        <h5 className="title-text mb-2">{post.title}</h5>
                      </Link>
                      <p className="card-text text-secondary">{post.desc}</p>
                    </CardBody>
                  </Card>
                </Col>
              ))}
            </Row>
          </Col>
          <Col lg="4" xxl="3">
            <Row>
              <Col md="12">
                {/* About Me */}
                <Card>
                  <CardHeader>
                    <h5>About Me</h5>
                  </CardHeader>
                  <CardBody>
                    <div className="text-secondary mb-3">
                      <p className="mb-3">Hi! I am Aaliyah.</p>
                      <p>
                        Over the last fifteen years of my career, I earned a
                        sense of creativity. I want to show the beauty of life
                        in a chaotic world.
                      </p>
                      <p>
                        In my first year as a photographer, I thought the photos
                        I took needed more spark. So I enrolled in a graphic
                        design class. I combined my photographs with visual arts
                        and finally saw what I was looking for. The spark!
                      </p>
                    </div>

                    <div className="d-flex gap-2">
                      <Button color="facebook" className="icon-btn b-r-5">
                        <IconBrandFacebook size={18} color="white" />
                      </Button>
                      <Button color="success" className="icon-btn b-r-5">
                        <i className="ti ti-brand-whatsapp text-white"></i>
                      </Button>
                      <Button color="info" className="icon-btn b-r-5">
                        <i className="ti ti-brand-twitter text-white"></i>
                      </Button>
                      <Button color="dark" className="icon-btn b-r-5">
                        <IconBrandGithub size={18} color="white" />
                      </Button>
                    </div>
                  </CardBody>
                </Card>
              </Col>

              {/* Categories */}
              <Col md="12">
                <Card className="equal-card">
                  <CardHeader>
                    <h5>Categories</h5>
                  </CardHeader>
                  <CardBody>
                    <ListGroup>
                      {blogDetailsCategories.map((cat) => (
                        <ListGroupItem
                          key={cat.label}
                          className="d-flex justify-content-between align-items-start"
                        >
                          <div className="me-auto">
                            <p className={`text-${cat.color} fw-semibold mb-0`}>
                              <i className="ti ti-chevron-right me-1"></i>
                              {cat.label}
                            </p>
                          </div>
                          <span>[{cat.count}]</span>
                        </ListGroupItem>
                      ))}
                    </ListGroup>
                  </CardBody>
                </Card>
              </Col>

              {/* Popular Blog Posts */}
              <Col md="12">
                <Card>
                  <CardHeader>
                    <h5>Popular Blog Posts</h5>
                  </CardHeader>
                  <CardBody>
                    {popularPosts.map((post, i) => (
                      <div
                        key={i}
                        className="position-relative mb-3 d-flex align-items-start"
                      >
                        <img
                          src={post.img}
                          alt=""
                          className={`position-absolute w-40 h-40 bg-${post.bg} rounded top-0`}
                        />
                        <div className="ms-5">
                          <p className="text-dark mb-0 fw-semibold small">
                            {post.text}
                          </p>
                          <div className="text-secondary text-end small">
                            {post.time}
                          </div>
                        </div>
                      </div>
                    ))}
                    <div className="mt-3">
                      <Link href="/apps/blog-page/blog" target="_blank">
                        <Button color="primary" className="rounded w-100">
                          <i className="ti ti-plus me-1"></i> View All
                        </Button>
                      </Link>
                    </div>
                  </CardBody>
                </Card>
              </Col>

              {/* Tags */}
              <Col md="12">
                <Card>
                  <CardHeader>
                    <h5>Popular Blog Tags</h5>
                  </CardHeader>
                  <CardBody>
                    <div className="d-flex flex-wrap gap-2 fs-6">
                      {blogTags.map((tag) => (
                        <span key={tag} className="badge text-light-dark">
                          {tag}
                        </span>
                      ))}
                    </div>
                  </CardBody>
                </Card>
              </Col>

              {/* Subscribe */}
              <Col md="12">
                <Card>
                  <CardHeader>
                    <h5>Subscribe</h5>
                  </CardHeader>
                  <CardBody>
                    <Form action={subscribeNewsletter}>
                      <FormGroup className="mb-3">
                        <Label for="email" className="fw-semibold">
                          Email
                        </Label>
                        <div className="input-group">
                          <Input
                            id="email"
                            type="email"
                            name="email"
                            className="form-control-sm"
                            placeholder="@gmail.com"
                            value={email}
                            onChange={(e) => setEmail(e.target.value)}
                            required
                          />
                          <Button type="submit" color="primary">
                            <i className="ti ti-mail-fast fs-5"></i>
                          </Button>
                        </div>
                      </FormGroup>
                      <FormGroup>
                        <p className="text-success">
                          Subscribe to our newsletter and stay Updated
                        </p>
                      </FormGroup>
                    </Form>
                  </CardBody>
                </Card>
              </Col>
            </Row>
          </Col>
        </Row>
      </Container>
      <GLightbox />
    </div>
  );
};

export default BlogDetailsPage;
